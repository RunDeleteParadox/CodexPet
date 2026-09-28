// SPDX-License-Identifier: AGPL-3.0-or-later
// CodexPet additions: bounded, allocation-free serial protocol v1 parser.
#pragma once
#include <stddef.h>
#include <stdint.h>

namespace codexpet {
constexpr unsigned kProtocolVersion = 1;
constexpr const char* kFirmwareVersion = "1.1.7";
constexpr const char* kExpressions[] = {
    "idle", "listening", "thinking", "happy", "excited", "curious",
    "confused", "angry", "surprised", "sad", "sleepy", "dizzy", "working", "waiting", "success", "error", "stop", "wake"};
constexpr const char* kSuccessSounds[] = {"none", "fanfare", "voice"};
enum class SuccessSound : unsigned { None, Fanfare, Voice };
enum class CommandType { Unknown, Info, Version, Ping, Status, State, Reaction, Brightness, Sleep, Wake, Sound };
struct Command { CommandType type; unsigned value; };

constexpr bool equal(const char* input, size_t length, const char* expected) {
  size_t i = 0;
  while (i < length && expected[i] && input[i] == expected[i]) ++i;
  return i == length && expected[i] == '\0';
}
constexpr bool prefix(const char* input, size_t length, const char* expected, size_t count) {
  if (length < count) return false;
  for (size_t i = 0; i < count; ++i) if (input[i] != expected[i]) return false;
  return true;
}
constexpr Command parse(const char* input, size_t length) {
  if (!input || length == 0 || length > 63) return {CommandType::Unknown, 0};
  if (equal(input, length, "INFO")) return {CommandType::Info, 0};
  if (equal(input, length, "VERSION")) return {CommandType::Version, 0};
  if (equal(input, length, "PING")) return {CommandType::Ping, 0};
  if (equal(input, length, "STATUS")) return {CommandType::Status, 0};
  if (equal(input, length, "SLEEP")) return {CommandType::Sleep, 0};
  if (equal(input, length, "WAKE")) return {CommandType::Wake, 0};
  if (prefix(input, length, "SOUND ", 6)) {
    for (unsigned i = 0; i < 3; ++i)
      if (equal(input + 6, length - 6, kSuccessSounds[i])) return {CommandType::Sound, i};
  }
  if (prefix(input, length, "STATE ", 6)) {
    for (unsigned i = 0; i < sizeof(kExpressions) / sizeof(kExpressions[0]); ++i)
      if (equal(input + 6, length - 6, kExpressions[i])) return {CommandType::State, i};
  }
  if (prefix(input, length, "REACT ", 6)) {
    for (unsigned i = 14; i < 18; ++i)
      if (equal(input + 6, length - 6, kExpressions[i])) return {CommandType::Reaction, i};
  }
  if (prefix(input, length, "BRIGHTNESS ", 11) && length > 11 && length <= 14) {
    unsigned value = 0;
    for (size_t i = 11; i < length; ++i) {
      if (input[i] < '0' || input[i] > '9') return {CommandType::Unknown, 0};
      value = value * 10 + static_cast<unsigned>(input[i] - '0');
    }
    if (value <= 100) return {CommandType::Brightness, value};
  }
  return {CommandType::Unknown, 0};
}
}  // namespace codexpet
