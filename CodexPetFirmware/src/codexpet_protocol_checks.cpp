// SPDX-License-Identifier: AGPL-3.0-or-later
// Compile-time regression tests run by every pio run. No emulated hardware.
#include "codexpet_protocol.h"
using namespace codexpet;
static_assert(parse("INFO", 4).type == CommandType::Info, "INFO");
static_assert(parse("VERSION", 7).type == CommandType::Version, "VERSION");
static_assert(parse("PING", 4).type == CommandType::Ping, "PING");
static_assert(parse("STATUS", 6).type == CommandType::Status, "STATUS");
static_assert(parse("SLEEP", 5).type == CommandType::Sleep, "SLEEP");
static_assert(parse("WAKE", 4).type == CommandType::Wake, "WAKE");
static_assert(parse("STATE thinking", 14).value == 2, "thinking index");
static_assert(parse("STATE dizzy", 11).value == 11, "last expression");
static_assert(parse("STATE sleeping", 14).type == CommandType::Unknown, "unknown expression");
static_assert(parse("STATE idle extra", 16).type == CommandType::Unknown, "extra argument");
static_assert(parse("BRIGHTNESS 0", 12).type == CommandType::Brightness, "black");
static_assert(parse("BRIGHTNESS 100", 14).value == 100, "maximum");
static_assert(parse("BRIGHTNESS 101", 14).type == CommandType::Unknown, "range");
static_assert(parse("BRIGHTNESS -1", 13).type == CommandType::Unknown, "negative");
static_assert(parse("BRIGHTNESS 1x", 13).type == CommandType::Unknown, "not numeric");
static_assert(parse("BRIGHTNESS ", 11).type == CommandType::Unknown, "missing number");
static_assert(parse("INFO\nSLEEP", 10).type == CommandType::Unknown, "injected line");
static_assert(parse("INFOx", 5).type == CommandType::Unknown, "prefix is insufficient");
static_assert(parse("", 0).type == CommandType::Unknown, "empty");
static_assert(parse(nullptr, 0).type == CommandType::Unknown, "null");
static_assert(parse("X", 64).type == CommandType::Unknown, "oversize before access");

static_assert(parse("STATE working",13).value==12,"working enum");
static_assert(parse("STATE waiting",13).value==13,"waiting enum");
static_assert(parse("REACT success",13).value==14,"success reaction");
static_assert(parse("REACT error",11).value==15,"error reaction");
static_assert(parse("REACT stop",10).value==16,"neutral stop reaction");
static_assert(parse("REACT wake",10).value==17,"wake reaction");
static_assert(parse("REACT working",13).type==CommandType::Unknown,"base is not reaction");
static_assert(parse("REACT success x",15).type==CommandType::Unknown,"reaction arguments rejected");
static_assert(parse("REACT BigSuccess",16).type==CommandType::Unknown,"no fabricated celebration");
static_assert(parse("SOUND none",10).type==CommandType::Sound,"silent setting");
static_assert(parse("SOUND none",10).value==static_cast<unsigned>(SuccessSound::None),"silent index");
static_assert(parse("SOUND fanfare",13).value==static_cast<unsigned>(SuccessSound::Fanfare),"two-note cue");
static_assert(parse("SOUND voice",11).value==static_cast<unsigned>(SuccessSound::Voice),"approved voice");
static_assert(parse("SOUND Voice",11).type==CommandType::Unknown,"sound names case sensitive");
static_assert(parse("SOUND voice x",13).type==CommandType::Unknown,"no extra sound arguments");
static_assert(parse("SOUND ",6).type==CommandType::Unknown,"missing sound");
static_assert(parse("SOUND 2",7).type==CommandType::Unknown,"no numeric sound alias");
