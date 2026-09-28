// SPDX-License-Identifier: AGPL-3.0-or-later

#include <Arduino.h>
#include <M5IOE1.h>
#include <M5Unified.h>
#include <esp_rom_sys.h>
#include <esp_system.h>
#include <esp_timer.h>

#include "avatar_engine.h"
#include "codexpet_protocol.h"
#include "success_audio_data.h"

namespace {

constexpr uint8_t kIoeAddress = 0x4F;
constexpr uint8_t kVibrationPwmRegister = 0x1B;
constexpr uint32_t kIoeBusFrequency = M5IOE1_I2C_FREQ_100K;
constexpr uint32_t kDiagnosticRefreshIntervalMs = 100;
constexpr uint32_t kImuInteractionIntervalMs = 20;
constexpr uint32_t kShakeReleaseDelayMs = 650;
constexpr uint32_t kShakeSequenceTimeoutMs = 1500;
constexpr uint32_t kShakeMaximumSwingGapMs = 480;
constexpr uint32_t kShakeMinimumSwingGapMs = 90;
constexpr float kStrongShakeAxisDelta = 0.32f;
constexpr float kStrongShakeJerk = 0.50f;
constexpr uint8_t kRequiredShakeSwings = 4;
constexpr uint16_t kImuCalibrationSamples = 30;
constexpr int16_t kGestureDirectionLockPx = 12;
constexpr int16_t kGestureCommitPx = 52;
constexpr uint16_t kSwipeTransitionMs = 160;

enum class GestureAxis : uint8_t { None, Horizontal, Vertical };

M5IOE1 ioe;
AvatarEngine avatar;
bool vibrationReady = false;
bool diagnosticMode = false;
bool diagnosticToggleLatched = false;
uint32_t vibrationStopsAtMs = 0;
uint32_t lastDiagnosticRefreshMs = 0;
uint32_t lastImuInteractionMs = 0;
uint32_t shakeQuietStartedMs = 0;
uint16_t imuCalibrationCount = 0;
float filteredAccelX = 0.0f;
float filteredAccelY = 0.0f;
float neutralAccelX = 0.0f;
float neutralAccelY = 0.0f;
float previousAccelX = 0.0f;
float previousAccelY = 0.0f;
float previousAccelZ = 0.0f;
float shakeEnergy = 0.0f;
bool shakeReactionActive = false;
uint8_t shakeSwingCount = 0;
int8_t previousShakeDirection = 0;
uint32_t shakeSequenceStartedMs = 0;
uint32_t lastStrongShakeMs = 0;
String lastDiagnosticEvent = "Waiting for input";
String serialCommand;
// CodexPet control state. USB remains active while the panel sleeps.
bool serialOverflow = false;
bool petSleeping = false;
bool panelAsleep = false;
bool petWaking = false;
uint32_t sleepStartedMs = 0;
uint32_t wakeStartedMs = 0;
uint8_t petBrightness = 60;
codexpet::SuccessSound successSound = codexpet::SuccessSound::None;
bool audioReady = false;
uint32_t audioPlayCount = 0;
uint32_t audioErrorCount = 0;
int lastPanelBrightness = -1;
ExpressionId requestedExpression = ExpressionId::Idle;
char petDeviceId[15] = {};
char petBootId[17] = {};
const char* lastPowerCommand = "boot";
uint64_t lastPowerCommandAtMs = 0;
GestureAxis gestureAxis = GestureAxis::None;
bool gestureCommitted = false;

bool reached(uint32_t now, uint32_t deadline) {
  return deadline != 0 && static_cast<int32_t>(now - deadline) >= 0;
}

void resetShakeSequence() {
  shakeSwingCount = 0;
  previousShakeDirection = 0;
  shakeSequenceStartedMs = 0;
  lastStrongShakeMs = 0;
}

void setFont() {
  M5.Display.setFont(&fonts::efontCN_16);
  M5.Display.setTextSize(1);
  M5.Display.setTextDatum(top_left);
}

void drawDiagnosticLine(int y, const String& label, const String& value,
                        uint16_t color = TFT_WHITE) {
  M5.Display.fillRect(46, y, 374, 22, TFT_BLACK);
  M5.Display.setTextColor(TFT_DARKGREY, TFT_BLACK);
  M5.Display.drawString(label, 46, y);
  M5.Display.setTextColor(color, TFT_BLACK);
  M5.Display.drawString(value, 156, y);
}

void drawDiagnosticFrame() {
  M5.Display.fillScreen(TFT_BLACK);
  const int cx = M5.Display.width() / 2;
  const int cy = M5.Display.height() / 2;
  M5.Display.drawCircle(cx, cy, min(cx, cy) - 3, TFT_DARKGREY);
  M5.Display.drawCircle(cx, cy, min(cx, cy) - 12, TFT_NAVY);

  setFont();
  M5.Display.setTextDatum(top_center);
  M5.Display.setTextColor(TFT_CYAN, TFT_BLACK);
  M5.Display.drawString("Hardware Check", cx, 45);
  M5.Display.setTextColor(TFT_DARKGREY, TFT_BLACK);
  M5.Display.drawString("Hold A+B to return", cx, 72);
  M5.Display.setTextDatum(top_left);

  drawDiagnosticLine(112, "Display",
                     String(M5.Display.width()) + " x " + String(M5.Display.height()),
                     TFT_GREEN);
  drawDiagnosticLine(146, "IMU", "initializing...");
  drawDiagnosticLine(180, "Touch", M5.Touch.isEnabled() ? "enabled" : "not detected");
  drawDiagnosticLine(214, "Vibration", vibrationReady ? "ready" : "not detected");
  drawDiagnosticLine(264, "Last event", lastDiagnosticEvent, TFT_YELLOW);

  M5.Display.setTextDatum(top_center);
  M5.Display.setTextColor(TFT_LIGHTGREY, TFT_BLACK);
  M5.Display.drawString("A: vibrate", cx, 340);
  M5.Display.drawString("B: redraw", cx, 370);
  M5.Display.drawString("Touch: show coordinates", cx, 400);
  M5.Display.setTextDatum(top_left);
}

void setDiagnosticEvent(const String& event) {
  lastDiagnosticEvent = event;
  drawDiagnosticLine(264, "Last event", lastDiagnosticEvent, TFT_YELLOW);
  Serial.println(event);
}

void stopVibration() {
  if (!vibrationReady) return;
  const uint8_t pwmData[2] = {0, 0x80};
  M5.In_I2C.writeRegister(kIoeAddress, kVibrationPwmRegister, pwmData,
                          sizeof(pwmData), kIoeBusFrequency);
  vibrationStopsAtMs = 0;
}

void startVibration(uint8_t strength = 160, uint16_t durationMs = 45) {
  if (!vibrationReady) return;
  const uint16_t duty12 =
      static_cast<uint16_t>(strength) * 0x0FFF / 0xFF;
  const uint8_t pwmData[2] = {
      static_cast<uint8_t>(duty12 & 0xFF),
      static_cast<uint8_t>(0x80 | ((duty12 >> 8) & 0x0F)),
  };
  if (!M5.In_I2C.writeRegister(kIoeAddress, kVibrationPwmRegister, pwmData,
                               sizeof(pwmData), kIoeBusFrequency)) {
    Serial.println("Vibration PWM write failed");
    return;
  }
  vibrationStopsAtMs = millis() + durationMs;
}

void updateVibration(uint32_t nowMs) {
  if (reached(nowMs, vibrationStopsAtMs)) stopVibration();
}

void setupVibration() {
  const m5ioe1_err_t error =
      ioe.begin(&M5.In_I2C, kIoeAddress, M5IOE1_I2C_FREQ_100K);
  vibrationReady = error == M5IOE1_OK;
  if (vibrationReady) {
    ioe.setPwmFrequency(200);
    // Configure the PWM pin once through the verified driver. Runtime haptics
    // then need only one two-byte register write instead of a write/readback/
    // GPIO-mode sequence that can stall an animation frame.
    vibrationReady =
        ioe.setPwmDuty12bit(M5IOE1_PWM_CH1, 0, false, true) == M5IOE1_OK;
  }
}

void trigger(ExpressionId expression, uint32_t nowMs,
             uint16_t firstTransitionMs = 0) {
  avatar.show(expression, nowMs, true, firstTransitionMs);
  startVibration();
  Serial.printf("Expression: %s\n", avatar.activeName());
}

void handleProductTouch(uint32_t nowMs) {
  if (!M5.Touch.isEnabled()) return;
  const auto touch = M5.Touch.getDetail(0);

  if (touch.wasPressed()) {
    gestureAxis = GestureAxis::None;
    gestureCommitted = false;
  }

  if (touch.wasHold() && gestureAxis == GestureAxis::None &&
      !gestureCommitted) {
    gestureCommitted = true;
    avatar.releaseTouch();
    trigger(ExpressionId::Angry, nowMs);
    return;
  }

  if (touch.isPressed()) {
    const bool moving = touch.isFlicking() || touch.isDragging();
    const int deltaX = moving ? touch.distanceX() : 0;
    const int deltaY = moving ? touch.distanceY() : 0;

    if (gestureAxis == GestureAxis::None &&
        std::max(abs(deltaX), abs(deltaY)) >= kGestureDirectionLockPx) {
      gestureAxis = abs(deltaX) >= abs(deltaY) ? GestureAxis::Horizontal
                                               : GestureAxis::Vertical;
    }

    if (gestureAxis == GestureAxis::Horizontal) {
      avatar.releaseTouch();
      const int8_t direction = deltaX > 0 ? -1 : 1;
      avatar.setSwipeOffset(deltaX, 0.0f,
                            gestureCommitted ? 0 : direction);
      if (!gestureCommitted && abs(deltaX) >= kGestureCommitPx) {
        avatar.commitSwipe(direction, nowMs, kSwipeTransitionMs);
        gestureCommitted = true;
        startVibration(125, 35);
        Serial.printf("Swipe commit: %s dx=%d\n", avatar.activeName(),
                      deltaX);
      }
      return;
    }

    if (gestureAxis == GestureAxis::Vertical) {
      avatar.releaseTouch();
      avatar.setSwipeOffset(0.0f, deltaY, 0);
      if (!gestureCommitted && abs(deltaY) >= kGestureCommitPx) {
        trigger(deltaY > 0 ? ExpressionId::Sleepy
                           : ExpressionId::Surprised,
                nowMs, kSwipeTransitionMs);
        gestureCommitted = true;
        Serial.printf("Vertical swipe commit: %s dy=%d\n",
                      avatar.activeName(), deltaY);
      }
      return;
    }

    if (!gestureCommitted) avatar.setTouchTarget(touch.x, touch.y);
  }

  if (touch.wasReleased()) {
    const bool consumed =
        gestureCommitted || gestureAxis != GestureAxis::None;
    avatar.releaseTouch();
    avatar.releaseSwipe();
    gestureAxis = GestureAxis::None;
    gestureCommitted = false;
    if (consumed) return;
  }

  if (gestureCommitted) {
    return;
  }

  if (touch.wasClicked()) {
    trigger(touch.getClickCount() >= 2 ? ExpressionId::Surprised
                                       : ExpressionId::Happy,
            nowMs);
  }
}

void handleImuInteraction(uint32_t nowMs) {
  if (nowMs - lastImuInteractionMs < kImuInteractionIntervalMs) return;
  lastImuInteractionMs = nowMs;
  if (!M5.Imu.update()) return;

  const auto imu = M5.Imu.getImuData();
  // The official StopWatch demo swaps the BMI270 sensor's raw X/Y values to
  // obtain screen coordinates. Keep all avatar interaction in that same
  // coordinate system: X is screen-left/right and Y is screen-up/down.
  const float screenAccelX = imu.accel.y;
  const float screenAccelY = imu.accel.x;
  const float screenAccelZ = imu.accel.z;
  const float screenGyroX = imu.gyro.y;
  const float screenGyroY = imu.gyro.x;
  constexpr float kFilterAmount = 0.32f;
  if (imuCalibrationCount == 0) {
    filteredAccelX = screenAccelX;
    filteredAccelY = screenAccelY;
    previousAccelX = screenAccelX;
    previousAccelY = screenAccelY;
    previousAccelZ = screenAccelZ;
  } else {
    filteredAccelX += (screenAccelX - filteredAccelX) * kFilterAmount;
    filteredAccelY += (screenAccelY - filteredAccelY) * kFilterAmount;
  }

  if (imuCalibrationCount < kImuCalibrationSamples) {
    neutralAccelX += screenAccelX / kImuCalibrationSamples;
    neutralAccelY += screenAccelY / kImuCalibrationSamples;
    ++imuCalibrationCount;
    if (imuCalibrationCount == kImuCalibrationSamples) {
      Serial.printf("IMU interaction ready: neutral x=%.2f y=%.2f\n",
                    neutralAccelX, neutralAccelY);
    }
  } else {
    // Roughly 0.28 g of tilt reaches full gaze travel. Neutral adaptation is
    // deliberately very slow so eyes keep looking in the chosen direction
    // while the user holds the device at an angle. Gyroscope feed-forward
    // makes the eyes lead during rotation; gravity keeps the final direction.
    neutralAccelX += (filteredAccelX - neutralAccelX) * 0.00015f;
    neutralAccelY += (filteredAccelY - neutralAccelY) * 0.00015f;
    avatar.setTiltTarget(-(filteredAccelX - neutralAccelX) / 0.28f,
                         -(filteredAccelY - neutralAccelY) / 0.28f,
                         -screenGyroY / 180.0f, screenGyroX / 180.0f);
  }

  const float deltaX = screenAccelX - previousAccelX;
  const float deltaY = screenAccelY - previousAccelY;
  const float deltaZ = screenAccelZ - previousAccelZ;
  const float jerk = fabsf(deltaX) + fabsf(deltaY) + fabsf(deltaZ);
  previousAccelX = screenAccelX;
  previousAccelY = screenAccelY;
  previousAccelZ = screenAccelZ;
  shakeEnergy = shakeEnergy * 0.72f + jerk * 0.28f;

  if (imuCalibrationCount < kImuCalibrationSamples) return;

  const float shakeIntensity = std::max(
      0.0f, std::min(1.0f, (shakeEnergy - 0.055f) / 0.48f));
  const float shakeDirectionX =
      std::max(-1.0f, std::min(1.0f, -deltaX / 0.28f));
  const float shakeDirectionY =
      std::max(-1.0f, std::min(1.0f, -deltaY / 0.28f));
  avatar.setShakeTarget(shakeDirectionX, shakeDirectionY, shakeIntensity);

  if (!shakeReactionActive) {
    if (shakeSwingCount > 0 &&
        (nowMs - shakeSequenceStartedMs > kShakeSequenceTimeoutMs ||
         nowMs - lastStrongShakeMs > kShakeMaximumSwingGapMs)) {
      resetShakeSequence();
    }

    if (shakeSwingCount == 0 && jerk >= kStrongShakeJerk) {
      if (fabsf(deltaX) >= kStrongShakeAxisDelta) {
        shakeSwingCount = 1;
        previousShakeDirection = deltaX >= 0.0f ? 1 : -1;
        shakeSequenceStartedMs = nowMs;
        lastStrongShakeMs = nowMs;
        Serial.printf("IMU horizontal swing 1/%u delta=%.2f\n",
                      kRequiredShakeSwings, deltaX);
      }
    } else if (shakeSwingCount > 0) {
      const int8_t direction = deltaX >= 0.0f ? 1 : -1;
      const uint32_t swingGapMs = nowMs - lastStrongShakeMs;
      const bool strongEnough = jerk >= kStrongShakeJerk &&
                                fabsf(deltaX) >= kStrongShakeAxisDelta;
      if (strongEnough && direction != previousShakeDirection &&
          swingGapMs >= kShakeMinimumSwingGapMs &&
          swingGapMs <= kShakeMaximumSwingGapMs) {
        ++shakeSwingCount;
        previousShakeDirection = direction;
        lastStrongShakeMs = nowMs;
        Serial.printf("IMU horizontal swing %u/%u delta=%.2f gap=%lums\n",
                      shakeSwingCount, kRequiredShakeSwings, deltaX,
                      static_cast<unsigned long>(swingGapMs));
      }
    }

    if (shakeSwingCount >= kRequiredShakeSwings) {
      shakeReactionActive = true;
      shakeQuietStartedMs = 0;
      resetShakeSequence();
      avatar.play(ExpressionId::Dizzy, nowMs,
                  AvatarEngine::PlaybackMode::Loop, false, 150);
      startVibration(190, 80);
      Serial.printf("IMU repeated horizontal shake -> DIZZY energy=%.2f\n",
                    shakeEnergy);
    }
    return;
  }

  if (shakeIntensity > 0.10f) {
    shakeQuietStartedMs = 0;
    return;
  }

  if (shakeQuietStartedMs == 0) {
    shakeQuietStartedMs = nowMs;
  } else if (nowMs - shakeQuietStartedMs >= kShakeReleaseDelayMs) {
    shakeReactionActive = false;
    shakeQuietStartedMs = 0;
    resetShakeSequence();
    if (avatar.activeExpression() == ExpressionId::Dizzy) {
      avatar.show(avatar.baseExpression(), nowMs, false, 260);
    }
    Serial.println("IMU shake settled -> BASE");
  }
}

void setPetBrightness(uint8_t raw) {
  if (lastPanelBrightness == raw) return;
  M5.Display.setBrightness(raw);
  lastPanelBrightness = raw;
}

void updatePetPower(uint32_t nowMs) {
  const unsigned brightness = static_cast<unsigned>(petBrightness) * 255 / 100;
  if (petSleeping) {
    const uint32_t elapsed = nowMs - sleepStartedMs;
    if (elapsed >= 1500) {
      if (!panelAsleep) {
        setPetBrightness(0);
        M5.Display.sleep();
        panelAsleep = true;
      }
    } else if (elapsed >= 1000) {
      setPetBrightness(brightness * (1500 - elapsed) / 500);
    }
  } else if (petWaking) {
    const uint32_t elapsed = nowMs - wakeStartedMs;
    setPetBrightness(brightness * std::min<uint32_t>(elapsed, 400) / 400);
    if (elapsed >= 400) petWaking = false;
  } else {
    setPetBrightness(brightness);
  }
}

void printPetInfo() {
  Serial.printf("CP {\"type\":\"info\",\"protocolVersion\":%u,\"deviceType\":\"CodexPet\",\"deviceId\":\"%s\",\"firmwareVersion\":\"%s\",\"successAudio\":true}\n",
                codexpet::kProtocolVersion, petDeviceId, codexpet::kFirmwareVersion);
}

void petAck(const char* command) {
  Serial.printf("CP {\"type\":\"ok\",\"command\":\"%s\"}\n", command);
}

const char* petResetReason() {
  switch (esp_reset_reason()) {
    case ESP_RST_POWERON: return "power-on";
    case ESP_RST_EXT: return "external";
    case ESP_RST_SW: return "software";
    case ESP_RST_PANIC: return "panic";
    case ESP_RST_INT_WDT: return "interrupt-watchdog";
    case ESP_RST_TASK_WDT: return "task-watchdog";
    case ESP_RST_WDT: return "watchdog";
    case ESP_RST_DEEPSLEEP: return "deep-sleep";
    case ESP_RST_BROWNOUT: return "brownout";
    case ESP_RST_SDIO: return "sdio";
    default: break;
  }
  // ESP-IDF 4.4 leaves USB resets unknown in the higher-level API.
  switch (esp_rom_get_reset_reason(0)) {
    case RESET_REASON_CORE_USB_UART: return "usb-uart";
    case RESET_REASON_CORE_USB_JTAG: return "usb-jtag";
    default: return "unknown";
  }
}

void printPetStatus() {
  // Read-only M5PM1 registers, same units/registers as the pinned M5Unified
  // driver. Keep the I2C success flag: a failed read must not become 0 V.
  uint8_t volts[4] = {};
  bool powerRead = false;
  if (M5.getBoard() == m5::board_t::board_M5StopWatch) {
    powerRead = M5.In_I2C.readRegister(0x6E, 0x22, volts, sizeof(volts), 100000);
    // The first read can wake an I2C-sleeping PM1; one bounded retry is safe.
    if (!powerRead) powerRead = M5.In_I2C.readRegister(0x6E, 0x22, volts, sizeof(volts), 100000);
  }
  const unsigned battery = static_cast<unsigned>(volts[1]) << 8 | volts[0];
  const unsigned usb = static_cast<unsigned>(volts[3]) << 8 | volts[2];
  char batteryJson[8] = "null", usbJson[8] = "null";
  if (powerRead && battery > 0 && battery <= 5500) snprintf(batteryJson, sizeof(batteryJson), "%u", battery);
  if (powerRead && usb <= 20000) snprintf(usbJson, sizeof(usbJson), "%u", usb);
  const char* usbPresent = !powerRead || usb > 20000 ? "null" : usb >= 4000 ? "true" : usb <= 800 ? "false" : "null";
  // StopWatch charging status is not implemented by this M5Unified version.
  Serial.printf("CP {\"type\":\"status\",\"state\":\"%s\",\"requestedState\":\"%s\",\"sleeping\":%s,\"brightness\":%u,\"diagnostic\":%s,"
                "\"freeHeap\":%u,\"minFreeHeap\":%u,\"bootId\":\"%s\",\"uptimeMs\":%llu,\"resetReason\":\"%s\",\"panelAsleep\":%s,"
                "\"lastPowerCommand\":\"%s\",\"lastPowerCommandAtMs\":%llu,\"batteryMillivolts\":%s,\"usbMillivolts\":%s,\"usbPresent\":%s,\"charging\":null,"
                "\"successSound\":\"%s\",\"audioReady\":%s,\"audioPlaying\":%s,\"audioPlayCount\":%u,\"audioErrorCount\":%u}\n",
                petSleeping ? "sleep" : codexpet::kExpressions[static_cast<unsigned>(avatar.activeExpression())],
                codexpet::kExpressions[static_cast<unsigned>(requestedExpression)],
                petSleeping ? "true" : "false", petBrightness, diagnosticMode ? "true" : "false",
                ESP.getFreeHeap(), ESP.getMinFreeHeap(), petBootId,
                static_cast<unsigned long long>(esp_timer_get_time() / 1000), petResetReason(), panelAsleep ? "true" : "false",
                lastPowerCommand, static_cast<unsigned long long>(lastPowerCommandAtMs), batteryJson, usbJson, usbPresent,
                codexpet::kSuccessSounds[static_cast<unsigned>(successSound)], audioReady ? "true" : "false",
                M5.Speaker.isPlaying() ? "true" : "false", audioPlayCount, audioErrorCount);
}

void playSuccessSound() {
  using codexpet::SuccessSound;
  if (successSound == SuccessSound::None) return;
  if (!audioReady) { ++audioErrorCount; return; }
  const bool voice = successSound == SuccessSound::Voice;
  // Static flash-backed WAVs outlive the asynchronous M5Unified playback.
  // Explicit channel and replacement avoid building a queue of old celebrations.
  const bool accepted = M5.Speaker.playWav(
      voice ? codexpet::kSuccessVoiceWav : codexpet::kSuccessFanfareWav,
      voice ? codexpet::kSuccessVoiceWavSize : codexpet::kSuccessFanfareWavSize,
      1, 0, true);
  if (accepted) ++audioPlayCount; else ++audioErrorCount;
}

bool handlePetCommand(const String& input, uint32_t nowMs) {
  using codexpet::CommandType;
  const auto command = codexpet::parse(input.c_str(), input.length());
  switch (command.type) {
    case CommandType::Info:
      printPetInfo();
      return true;
    case CommandType::Version:
      Serial.printf("CP {\"type\":\"version\",\"protocolVersion\":%u,\"firmwareVersion\":\"%s\"}\n",
                    codexpet::kProtocolVersion, codexpet::kFirmwareVersion);
      return true;
    case CommandType::Ping:
      Serial.println("CP {\"type\":\"pong\"}");
      return true;
    case CommandType::Status:
      printPetStatus();
      return true;
    case CommandType::State: {
      M5.Speaker.stop();
      const auto next = static_cast<ExpressionId>(command.value);
      const bool changed = requestedExpression != next || diagnosticMode;
      requestedExpression = next;
      if (diagnosticMode) avatar.invalidate();
      diagnosticMode = false;
      if (!petSleeping && (changed || avatar.baseExpression() != next || avatar.activeExpression() != next)) avatar.setPersistentState(next, nowMs);
      petAck("STATE");
      return true;
    }
    case CommandType::Reaction:
      // No Codex semantics here: play the Hub's graphical request once.
      M5.Speaker.stop();
      if (!petSleeping) {
        if (diagnosticMode) avatar.invalidate();
        diagnosticMode = false;
        avatar.play(static_cast<ExpressionId>(command.value), nowMs, AvatarEngine::PlaybackMode::Once, true);
        if (static_cast<ExpressionId>(command.value) == ExpressionId::Success) playSuccessSound();
      }
      petAck("REACT");
      return true;
    case CommandType::Sound: {
      const auto next = static_cast<codexpet::SuccessSound>(command.value);
      if (successSound != next) M5.Speaker.stop();
      successSound = next;
      petAck("SOUND");
      return true;
    }
    case CommandType::Brightness:
      petBrightness = command.value;
      updatePetPower(nowMs);
      petAck("BRIGHTNESS");
      return true;
    case CommandType::Sleep:
      M5.Speaker.stop();
      lastPowerCommand = "sleep";
      lastPowerCommandAtMs = esp_timer_get_time() / 1000;
      if (!petSleeping) {
        if (diagnosticMode) avatar.invalidate();
        diagnosticMode = false;
        petSleeping = true;
        petWaking = false;
        sleepStartedMs = nowMs;
        avatar.releaseTouch();
        avatar.releaseSwipe();
        avatar.setShakeTarget(0, 0, 0);
        avatar.setTiltTarget(0, 0);
        shakeReactionActive = false;
        resetShakeSequence();
        stopVibration();
        avatar.play(ExpressionId::Sleepy, nowMs, AvatarEngine::PlaybackMode::Once, false);
      }
      petAck("SLEEP");
      return true;
    case CommandType::Wake:
      lastPowerCommand = "wake";
      lastPowerCommandAtMs = esp_timer_get_time() / 1000;
      if (petSleeping) {
        petSleeping = false;
        petWaking = true;
        wakeStartedMs = nowMs;
        // Set cached brightness to zero BEFORE wakeup, avoiding a full-bright flash.
        setPetBrightness(0);
        if (panelAsleep) M5.Display.wakeup();
        panelAsleep = false;
        avatar.setPersistentState(requestedExpression, nowMs);
        avatar.invalidate();
      }
      petAck("WAKE");
      return true;
    default:
      return false;
  }
}

void handleSerialCommands(uint32_t nowMs) {
  while (Serial.available() > 0) {
    const char character = static_cast<char>(Serial.read());
    if (character == '\n' || character == '\r') {
      if (serialOverflow) {
        Serial.println("CP {\"type\":\"error\",\"code\":\"line_too_long\"}");
        serialOverflow = false;
        serialCommand = "";
        continue;
      }
      if (serialCommand.length() == 0) continue;
      if (handlePetCommand(serialCommand, nowMs)) {
        // Versioned commands are idempotent and do not vibrate.
      } else if (!petSleeping && !diagnosticMode && avatar.showFromCommand(serialCommand, nowMs)) {
        startVibration();
        Serial.printf("Command accepted: %s\n", avatar.activeName());
      } else {
        Serial.println("CP {\"type\":\"error\",\"code\":\"unknown_command\"}");
      }
      serialCommand = "";
    } else if (!serialOverflow && serialCommand.length() < 63) {
      serialCommand += character;
    } else {
      serialOverflow = true;
      serialCommand = "";
    }
  }
}

void refreshDiagnosticSensors() {
  if (M5.Imu.update()) {
    const auto imu = M5.Imu.getImuData();
    char values[64];
    snprintf(values, sizeof(values), "H %.2f  V %.2f  Z %.2f", imu.accel.y,
             imu.accel.x, imu.accel.z);
    drawDiagnosticLine(146, "IMU", values, TFT_GREEN);
  } else {
    drawDiagnosticLine(146, "IMU", "no data", TFT_RED);
  }

  if (M5.Touch.getCount() > 0) {
    const auto touch = M5.Touch.getDetail(0);
    drawDiagnosticLine(180, "Touch",
                       "x " + String(touch.x) + "  y " + String(touch.y),
                       TFT_GREEN);
  } else {
    drawDiagnosticLine(180, "Touch",
                       M5.Touch.isEnabled() ? "enabled" : "not detected",
                       M5.Touch.isEnabled() ? TFT_GREEN : TFT_RED);
  }
}

void setDiagnosticMode(bool enabled) {
  diagnosticMode = enabled;
  stopVibration();
  if (diagnosticMode) {
    drawDiagnosticFrame();
    Serial.println("Diagnostic mode entered");
  } else {
    avatar.invalidate();
    Serial.println("Expression mode entered");
  }
}

void handleDiagnosticInput(uint32_t nowMs) {
  if (M5.BtnA.wasClicked()) {
    setDiagnosticEvent("Button A clicked");
    startVibration(180, 70);
  }
  if (M5.BtnB.wasClicked()) {
    lastDiagnosticEvent = "Button B clicked";
    drawDiagnosticFrame();
    Serial.println(lastDiagnosticEvent);
  }

  const auto touch = M5.Touch.getDetail(0);
  if (touch.wasPressed()) {
    setDiagnosticEvent("Touch " + String(touch.x) + "," + String(touch.y));
  }

  if (nowMs - lastDiagnosticRefreshMs >= kDiagnosticRefreshIntervalMs) {
    lastDiagnosticRefreshMs = nowMs;
    refreshDiagnosticSensors();
  }
}

void handleDiagnosticToggle() {
  const bool bothHeld = M5.BtnA.pressedFor(1000) && M5.BtnB.pressedFor(1000);
  if (bothHeld && !diagnosticToggleLatched) {
    diagnosticToggleLatched = true;
    setDiagnosticMode(!diagnosticMode);
  } else if (!M5.BtnA.isPressed() && !M5.BtnB.isPressed()) {
    diagnosticToggleLatched = false;
  }
}

}  // namespace

void setup() {
  auto config = M5.config();
  config.serial_baudrate = 115200;
  config.internal_mic = false;
  M5.begin(config);
  Serial.begin(115200);
  snprintf(petBootId, sizeof(petBootId), "%08lX%08lX", static_cast<unsigned long>(esp_random()), static_cast<unsigned long>(esp_random()));
  uint8_t factoryMac[6] = {};
  if (esp_efuse_mac_get_default(factoryMac) == ESP_OK) {
    snprintf(petDeviceId, sizeof(petDeviceId), "%02X%02X-%02X%02X-%02X%02X",
             factoryMac[0], factoryMac[1], factoryMac[2], factoryMac[3], factoryMac[4], factoryMac[5]);
  }

  M5.Display.setRotation(0);
  setPetBrightness(static_cast<unsigned>(petBrightness) * 255 / 100);
  M5.Touch.setHoldThresh(650);
  M5.Touch.setFlickThresh(10);
  setupVibration();
  // Initialize the codec/task after the shared expander, outside animation.
  M5.Speaker.setVolume(128);
  audioReady = M5.Speaker.begin();

  if (!avatar.begin()) {
    M5.Display.fillScreen(TFT_BLACK);
    M5.Display.setTextColor(TFT_RED, TFT_BLACK);
    M5.Display.setTextDatum(middle_center);
    M5.Display.drawString("Avatar buffer failed", M5.Display.width() / 2,
                          M5.Display.height() / 2);
    Serial.println("Avatar sprite allocation failed");
  }

  Serial.println("Expression device started");
  Serial.println(
      "Commands: idle listening thinking happy excited curious confused "
      "angry surprised sad sleepy dizzy");
  Serial.println("Playback test: once|loop|pingpong <expression>");
  Serial.println("Hold A+B for hardware diagnostics");
}

void loop() {
  M5.update();
  const uint32_t nowMs = millis();
  updateVibration(nowMs);
  handleSerialCommands(nowMs);
  if (M5.Speaker.isPlaying() && (petSleeping || diagnosticMode || avatar.activeExpression() != ExpressionId::Success)) M5.Speaker.stop();
  updatePetPower(nowMs);
  if (petSleeping) {
    if (!panelAsleep) avatar.update(nowMs);
    delay(1);
    return;
  }
  handleDiagnosticToggle();

  if (diagnosticMode) {
    handleDiagnosticInput(nowMs);
  } else {
    if (M5.BtnA.wasClicked()) {
      avatar.previous(nowMs);
      startVibration(125, 35);
    }
    if (M5.BtnB.wasClicked()) {
      avatar.next(nowMs);
      startVibration(125, 35);
    }
    handleProductTouch(nowMs);
    handleImuInteraction(nowMs);
    avatar.update(nowMs);
  }

  // A short cooperative yield keeps input responsive without quantizing the
  // 60 fps renderer onto a coarse 5 ms loop cadence.
  delay(1);
}
