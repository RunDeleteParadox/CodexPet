// SPDX-License-Identifier: AGPL-3.0-or-later
#pragma once
#include <algorithm>
#include <cmath>
#include <stdint.h>

namespace codexpet {
// Canonical Idle proportions, in the existing 1000-unit design space.
constexpr float kBaseEyeWidth = 155;
constexpr float kBaseEyeHeight = 360;
constexpr float kBaseEyeHalfSpacing = 200; // Authored pose; the common render rule compacts the visible gap.
constexpr float kEyeGapRatio = .42f;
constexpr float kFocusedEyeGapRatio = .27f;
constexpr float kMinimumEyeGap = 28;
constexpr uint32_t kWorkingPassMs = 7360;
constexpr uint32_t kWorkingCycleMs = 2*kWorkingPassMs;
inline float smoothUnit(float value) {
  const float p=std::min(1.0f,std::max(0.0f,value));
  return p*p*p*(p*(p*6-15)+10);
}
// Small fixed-size drawing samples. No heap, physics engine or scripting layer.
// The approved 466px study, sampled at 1.25x speed. Pen coordinates use the
// renderer's 1000-unit space; eye dimensions below retain the authored pixels.
constexpr float kStudyScale=.466f;
constexpr float kWritingStartX=(137-233)/kStudyScale;
constexpr float kWritingWidth=182/kStudyScale;
struct WritingPhrase { float strokes[4][3], hesitation[4], lift[4], gaze[4], pickup[4]; };
constexpr WritingPhrase kWritingPhrases[]={
  {{{.30f,.55f,1.90f},{.27f,2.15f,3.15f},{.18f,4.80f,5.65f},{.25f,5.82f,6.95f}},
   {1.91f,1.97f,2.04f,2.14f},{3.21f,3.41f,4.53f,4.77f},{3.33f,3.68f,4.19f,4.64f},{4.40f,4.62f,4.74f,4.98f}},
  {{{.25f,.62f,1.76f},{.34f,1.98f,3.18f},{.16f,4.71f,5.57f},{.25f,5.74f,6.91f}},
   {1.77f,1.82f,1.88f,1.98f},{3.24f,3.44f,4.44f,4.68f},{3.37f,3.69f,4.12f,4.53f},{4.31f,4.53f,4.65f,4.92f}}
};
inline float writingRamp(float t,float start,float end){return smoothUnit((t-start)/(end-start));}
inline float writingPulse(float t,float start,float up,float down,float end){
  return writingRamp(t,start,up)*(1-writingRamp(t,down,end));
}
inline float writingPulse(float t,const float (&v)[4]){return writingPulse(t,v[0],v[1],v[2],v[3]);}
inline float writingProgress(float t,const WritingPhrase& phrase){
  float p=0; for(const auto& stroke:phrase.strokes)p+=stroke[0]*writingRamp(t,stroke[1],stroke[2]); return p;
}
inline float writingY(float p,unsigned pass){
  const float phase=.2f+(pass%2)*.65f, detail=(pass%2)*.4f;
  return (177+2.7f*(sinf(p*56+phase)-sinf(phase))+.65f*(sinf(p*21+detail)-sinf(detail)))/kStudyScale;
}
struct WritingNib {float progress,x,y,angle;};
inline WritingNib writingNib(uint32_t elapsed){
  const unsigned pass=(elapsed/kWorkingPassMs)%2;
  const float t=(elapsed%kWorkingPassMs)*.00125f;
  const auto& q=kWritingPhrases[pass];
  const float p=writingProgress(t,q), back=writingRamp(t,7.68f,8.78f);
  const float hesitation=writingPulse(t,q.hesitation), ponder=writingPulse(t,q.lift);
  const float land=writingRamp(t,q.strokes[0][1]-.23f,q.strokes[0][1]);
  const float lift=15*(1-land)+3.5f*hesitation+17*ponder+15*writingRamp(t,6.99f,7.24f);
  const float slope=(2.7f*56*cosf(p*56+.2f+pass*.65f)+.65f*21*cosf(p*21+pass*.4f))/182;
  const float writing=.98f+atanf(slope)*.12f+.14f*ponder-.06f*hesitation;
  const float departure=1.06f+(writing-1.06f)*land;
  return {p,kWritingStartX+kWritingWidth*p*(1-back),
    writingY(p,pass)+(writingY(0,0)-writingY(p,pass))*back-(lift+56*back*(1-back))/kStudyScale,
    departure+(1.06f-departure)*writingRamp(t,7.03f,7.47f)};
}
struct WritingSample {
  float progress,x,y,angle,ink; unsigned cycle;
  float focus,ponder,lookX,lookY,roll,halfSpacing,lw,lh,rw,rh,ly,ry,leftAngle,rightAngle,blink;
};
inline WritingSample sampleWriting(uint32_t elapsed){
  const unsigned pass=(elapsed/kWorkingPassMs)%2;
  const float t=(elapsed%kWorkingPassMs)*.00125f;
  const float fullTime=(elapsed%kWorkingCycleMs)*.00125f;
  const auto& q=kWritingPhrases[pass];
  const auto nib=writingNib(elapsed), tracked=writingNib(elapsed>=60?elapsed-60:0);
  const float side=pass?1.0f:-1.0f;
  const float prep=writingPulse(t,0,.14f,.22f,.49f);
  const float ponder=writingPulse(t,q.gaze), reread=writingPulse(t,q.hesitation);
  const float focus=writingPulse(t,0,.50f,7.32f,9.02f)*(1-.91f*ponder)*(1-.10f*reread);
  const float takeUp=writingPulse(t,q.pickup), settle=writingPulse(t,.40f,.57f,.59f,.81f);
  const float inspect=writingPulse(t,7.14f,7.32f,7.51f,7.74f);
  const float leading=writingPulse(t,.25f,.40f,.50f,.72f)+takeUp;
  const float trackedX=tracked.x*kStudyScale+5; // screen x relative to 228px
  const float converge=9.6f*focus+1.1f*takeUp;
  const float aim=std::max(-3.2f,std::min(3.2f,-trackedX*.040f))*focus;
  const float squish=1-.026f*prep+.020f*settle-.013f*takeUp;
  const auto blinkAt=[t](float start,float length){
    if(t<start||t>start+length)return 1.0f;
    const float close=sinf(3.14159265359f*(t-start)/length); return 1-.955f*close*close;
  };
  return {nib.progress,nib.x,nib.y,nib.angle,1-writingRamp(t,7.82f,8.87f),pass,
    focus,ponder,trackedX*.24f*focus+side*28*ponder+3.5f*leading-6*inspect,
    28*focus-36*ponder-4*prep+2*settle+2*takeUp+sinf(2*3.14159265359f*fullTime/18.4f*4),
    side*2.8f*ponder+.9f*reread+.65f*sinf(fullTime*1.6f)*focus,62-7*focus,
    73+1.5f*prep,(165-25*focus+(side<0?-11:7)*ponder)*squish,
    74+1.5f*prep,(164-20*focus+(side<0?7:-11)*ponder+7*reread)*squish,
    3*focus-side*2*ponder,-2*focus+side*4*ponder,-converge+aim,converge+focus+aim,
    blinkAt(q.gaze[0]+.04f,.15f)*blinkAt(8.10f,.19f)};
}
struct ConfettiSample { float x, y, angle, width, height; bool visible; };
inline ConfettiSample sampleConfetti(unsigned index, uint32_t elapsed) {
  const float t = (static_cast<float>(elapsed) - 450 - index * 180) / 2600;
  const float side = index % 2 ? 1.0f : -1.0f;
  return {side*(150+(index%4)*48)+sinf(t*5+index)*28,
          -355.0f+(index%3)*22+465*t*t, index*0.8f+t*(7+index%3),
          19.0f+(index%3)*4, 10.0f+(index%2)*4, t>=0 && t<=1};
}
} // namespace codexpet
