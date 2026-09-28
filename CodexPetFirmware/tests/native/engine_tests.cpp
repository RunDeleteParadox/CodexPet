// SPDX-License-Identifier: AGPL-3.0-or-later
#include "avatar_engine.h"
#include "codexpet_protocol.h"
#include "expression_math.h"
#include <cstdio>
#include <cstdlib>
#include <new>
static size_t allocations=0;
void* operator new(size_t n){++allocations; if(void* p=malloc(n))return p;throw std::bad_alloc();}
void operator delete(void* p) noexcept{free(p);}
void operator delete(void* p,size_t) noexcept{free(p);}
void tick(AvatarEngine& a,unsigned duration){for(unsigned i=0;i<duration;i+=17){testClock+=17;a.update(testClock);}}
void check(bool ok,const char* name){if(!ok){printf("FAIL %s\n",name);exit(1);}printf("PASS %s\n",name);}
unsigned reactionDuration(ExpressionId id){switch(id){case ExpressionId::Success:return 5000;case ExpressionId::Error:return 3000;case ExpressionId::Stop:return 900;default:return 2000;}}
constexpr ExpressionId presentationExpressions[]={ExpressionId::Idle,ExpressionId::Thinking,ExpressionId::Working,ExpressionId::Waiting,ExpressionId::Success,ExpressionId::Error,ExpressionId::Stop,ExpressionId::Sleepy,ExpressionId::Wake};
unsigned previewDuration(ExpressionId id){switch(id){case ExpressionId::Idle:return 25340;case ExpressionId::Thinking:return 9200;case ExpressionId::Working:return codexpet::kWorkingCycleMs;case ExpressionId::Waiting:return 5930;case ExpressionId::Sleepy:return 2600;case ExpressionId::Success:case ExpressionId::Error:case ExpressionId::Stop:case ExpressionId::Wake:return reactionDuration(id);default:return 5000;}}
bool persistentExpression(ExpressionId id){return id==ExpressionId::Idle||id==ExpressionId::Listening||id==ExpressionId::Thinking||id==ExpressionId::Working||id==ExpressionId::Waiting;}

// Identical engines use the same clock and the adapter's deterministic blink
// intervals. Only the reference framebuffer is cleared before every frame.
// A differing pixel therefore exposes stale geometry outside the dirty rects.
bool dirtyRectsEraseEveryPixel(){
  AvatarEngine incremental,clean;
  if(!incremental.begin()||!clean.begin())return false;
  static std::array<uint16_t,466*466> previousFrame{};
  previousFrame.fill(TFT_BLACK);
  const auto compareFrames=[&](unsigned duration,const char* phase){
    for(unsigned elapsed=0;elapsed<duration;elapsed+=17){
      testClock+=17;
      M5.Display.pixels=previousFrame;
      incremental.update(testClock);
      previousFrame=M5.Display.pixels;
      M5.Display.fillScreen(TFT_BLACK);
      clean.update(testClock);
      if(M5.Display.pixels!=previousFrame){
        for(size_t pixel=0;pixel<previousFrame.size();++pixel)if(previousFrame[pixel]!=M5.Display.pixels[pixel]){
          printf("Dirty rect mismatch: %s %s +%u ms, pixel (%u,%u), incremental=%04x clean=%04x\n",incremental.activeName(),phase,elapsed,unsigned(pixel%466),unsigned(pixel/466),unsigned(previousFrame[pixel]),unsigned(M5.Display.pixels[pixel]));
          break;
        }
        return false;
      }
    }
    return true;
  };
  for(unsigned index=0;index<static_cast<unsigned>(ExpressionId::Count);++index){
    const auto id=static_cast<ExpressionId>(index);
    const auto mode=persistentExpression(id)?AvatarEngine::PlaybackMode::Loop:AvatarEngine::PlaybackMode::Once;
    incremental.play(id,testClock,mode,true);
    clean.play(id,testClock,mode,true);
    const unsigned duration=previewDuration(id)+(id==ExpressionId::Working?codexpet::kWorkingCycleMs:300);
    if(!compareFrames(duration,"playback"))return false;
    // Also compare erasure when a reaction or a pen disappears on interruption.
    incremental.play(id,testClock,mode,true);
    clean.play(id,testClock,mode,true);
    if(!compareFrames(std::min(1200u,previewDuration(id)/2),"before interruption"))return false;
    incremental.setPersistentState(ExpressionId::Waiting,testClock);
    clean.setPersistentState(ExpressionId::Waiting,testClock);
    if(!compareFrames(500,"interrupted by Waiting"))return false;
  }
  // Close, large silhouettes must also erase correctly under the existing
  // head projection and swipe preview, including interrupted special shapes.
  for(auto id:{ExpressionId::Waiting,ExpressionId::Success,ExpressionId::Error,
               ExpressionId::Stop,ExpressionId::Dizzy,ExpressionId::Surprised}){
    incremental.play(id,testClock,AvatarEngine::PlaybackMode::Loop,false);
    clean.play(id,testClock,AvatarEngine::PlaybackMode::Loop,false);
    if(!compareFrames(550,"before interaction"))return false;
    for(float direction:{1.0f,-1.0f}){
      incremental.setTiltTarget(.85f*direction,-.7f*direction,.2f*direction,-.15f*direction);
      clean.setTiltTarget(.85f*direction,-.7f*direction,.2f*direction,-.15f*direction);
      if(!compareFrames(500,"tilted"))return false;
    }
    incremental.setSwipeOffset(64,-18,1);
    clean.setSwipeOffset(64,-18,1);
    if(!compareFrames(300,"swipe preview"))return false;
    incremental.commitSwipe(1,testClock);
    clean.commitSwipe(1,testClock);
    if(!compareFrames(250,"swipe committed"))return false;
    incremental.releaseSwipe();clean.releaseSwipe();
    incremental.setTiltTarget(0,0);clean.setTiltTarget(0,0);
    incremental.setPersistentState(ExpressionId::Thinking,testClock);
    clean.setPersistentState(ExpressionId::Thinking,testClock);
    if(!compareFrames(700,"swipe released into Thinking"))return false;
  }
  return true;
}

// Count visible connected shapes rather than prescribing exact eye pixels.
// Eight-connected pixels tolerate diagonal feathered edges; tiny specks are
// ignored, but an isolated eyebrow is much larger than this threshold.
struct VisibleShape {
  int left=466,right=-1,top=466,bottom=-1;
  int height()const{return bottom-top+1;}
};
unsigned visibleShapeCount(std::array<VisibleShape,2>* eyeShapes=nullptr){
  static std::array<bool,466*466> visited{};
  static std::array<unsigned,466*466> pending{};
  visited.fill(false);
  unsigned shapes=0;
  for(unsigned pixel=0;pixel<visited.size();++pixel){
    if(visited[pixel]||M5.Display.pixels[pixel]==TFT_BLACK)continue;
    unsigned head=0,tail=1;pending[0]=pixel;visited[pixel]=true;
    VisibleShape bounds;
    while(head<tail){
      const unsigned current=pending[head++];
      const int x=current%466,y=current/466;
      bounds.left=std::min(bounds.left,x);bounds.right=std::max(bounds.right,x);
      bounds.top=std::min(bounds.top,y);bounds.bottom=std::max(bounds.bottom,y);
      for(int dy=-1;dy<=1;++dy)for(int dx=-1;dx<=1;++dx){
        const int nextX=x+dx,nextY=y+dy;
        if(nextX<0||nextX>=466||nextY<0||nextY>=466)continue;
        const unsigned next=nextY*466+nextX;
        if(!visited[next]&&M5.Display.pixels[next]!=TFT_BLACK){
          visited[next]=true;pending[tail++]=next;
        }
      }
    }
    if(tail>=9){
      if(eyeShapes&&shapes<eyeShapes->size())(*eyeShapes)[shapes]=bounds;
      ++shapes;
    }
  }
  if(eyeShapes&&shapes==2&&(*eyeShapes)[0].left>(*eyeShapes)[1].left)
    std::swap((*eyeShapes)[0],(*eyeShapes)[1]);
  return shapes;
}

bool thinkingUsesOnlyTwoEyes(){
  AvatarEngine entry;
  if(!entry.begin())return false;
  entry.play(ExpressionId::Angry,testClock,AvatarEngine::PlaybackMode::Once,false);
  tick(entry,650);
  if(visibleShapeCount()<4){
    printf("Thinking entry test requires the visible Angry eyebrows first\n");
    return false;
  }
  entry.setPersistentState(ExpressionId::Thinking,testClock);
  for(unsigned elapsed=17;elapsed<=2*previewDuration(ExpressionId::Thinking);elapsed+=17){
    tick(entry,17);
    // Check early transition frames as well as every pose and occasional blink.
    if(elapsed>204&&elapsed%119!=0)continue;
    const unsigned shapes=visibleShapeCount();
    if(shapes!=2){
      printf("Thinking should contain two eyes only at +%u ms; found %u shapes\n",elapsed,shapes);
      return false;
    }
  }
  return true;
}

bool workingConvergesAndErases(){
  AvatarEngine working; if(!working.begin())return false;
  working.setPersistentState(ExpressionId::Working,testClock);
  const unsigned started=testClock;
  for(unsigned sample:{1000u,7200u,8360u,14560u}){
    while(testClock-started<sample)tick(working,17);
    if(sample==7200||sample==14560){
      // The carried quill is already at the far left. No old ink may remain.
      for(int y=399;y<=430;++y)for(int x=200;x<=335;++x)
        if(M5.Display.pixels[y*466+x]!=TFT_BLACK)return false;
      continue;
    }
    for(unsigned side=0;side<2;++side){
      const int first=side?233:35,last=side?431:232;
      int top=466,bottom=0;
      for(int y=90;y<350;++y)for(int x=first;x<=last;++x)if(M5.Display.pixels[y*466+x]!=TFT_BLACK){top=std::min(top,y);bottom=std::max(bottom,y);}
      if(bottom-top<80)return false;
      float upper=0,lower=0;unsigned nUpper=0,nLower=0;
      for(int y=top+20;y<=bottom-20;++y)for(int x=first;x<=last;++x)if(M5.Display.pixels[y*466+x]!=TFT_BLACK){
        if(y<(top+bottom)/2){upper+=x;++nUpper;}else{lower+=x;++nLower;}
      }
      if(!nUpper||!nLower)return false;
      const float shift=lower/nLower-upper/nUpper;
      if((side==0&&shift<4)||(side==1&&shift>-4))return false;
    }
  }
  for(auto next:{ExpressionId::Thinking,ExpressionId::Waiting}){
    working.setPersistentState(next,testClock);tick(working,17);
    if(working.activeExpression()!=next)return false;
  }
  return true;
}

bool thinkingAlternatesItsSquint(){
  AvatarEngine thinking;
  if(!thinking.begin())return false;
  tick(thinking,400);
  thinking.setPersistentState(ExpressionId::Thinking,testClock);
  const unsigned started=testClock;
  // Sample inside the held poses, away from transitions and the deliberate
  // blink. Repeating them checks the loop without requiring pixel equality
  // across breathing or the scheduler's 17 ms keyframe quantization.
  for(unsigned elapsed:{1500u,4800u,10700u,14000u}){
    while(testClock-started<elapsed)tick(thinking,17);
    std::array<VisibleShape,2> eyes;
    if(visibleShapeCount(&eyes)!=2)return false;
    const bool leftSquints=(elapsed%previewDuration(ExpressionId::Thinking))<3000;
    const int small=eyes[leftSquints?0:1].height();
    const int large=eyes[leftSquints?1:0].height();
    if(small<30||large<100||small>large*.70f){
      printf("Thinking lost its %s squint at +%u ms: heights=%d,%d\n",
             leftSquints?"left":"right",elapsed,eyes[0].height(),eyes[1].height());
      return false;
    }
    if(thinking.activeExpression()!=ExpressionId::Thinking||thinking.baseExpression()!=ExpressionId::Thinking)return false;
  }
  return true;
}

bool thinkingLoopSettlesWithoutJump(){
  AvatarEngine thinking;
  if(!thinking.begin())return false;
  thinking.setPersistentState(ExpressionId::Thinking,testClock);
  tick(thinking,8500);
  std::array<VisibleShape,2> settled,restarted;
  if(visibleShapeCount(&settled)!=2)return false;
  tick(thinking,900);
  if(visibleShapeCount(&restarted)!=2)return false;
  for(unsigned side=0;side<2;++side){
    const auto& a=settled[side];const auto& b=restarted[side];
    // Broad silhouette limits catch a reset to symmetric Idle, not subtle
    // anticipation, breathing, or a few frames of clock drift at the seam.
    if(abs(a.height()-b.height())>30||abs(a.left+a.right-b.left-b.right)>60
        ||abs(a.top+a.bottom-b.top-b.bottom)>60){
      printf("Thinking loop jumped on eye %u: heights=%d,%d\n",side,a.height(),b.height());
      return false;
    }
  }
  return true;
}

bool thinkingHeldPosesRemainInterruptible(){
  AvatarEngine thinking;
  if(!thinking.begin())return false;
  for(unsigned phase:{1500u,4800u}){
    thinking.setPersistentState(ExpressionId::Thinking,testClock);tick(thinking,phase);
    const auto next=phase==1500?ExpressionId::Working:ExpressionId::Waiting;
    thinking.setPersistentState(next,testClock);
    if(thinking.activeExpression()!=next||thinking.baseExpression()!=next)return false;
    tick(thinking,34);
    if(thinking.activeExpression()!=next)return false;
  }
  // Returning from a reaction must restart the Thinking performance cleanly,
  // including its first asymmetric hold instead of a lingering reaction pose.
  thinking.setPersistentState(ExpressionId::Thinking,testClock);tick(thinking,4800);
  thinking.play(ExpressionId::Error,testClock,AvatarEngine::PlaybackMode::Once,true);
  tick(thinking,reactionDuration(ExpressionId::Error)+1800);
  std::array<VisibleShape,2> eyes;
  return thinking.activeExpression()==ExpressionId::Thinking
      &&thinking.baseExpression()==ExpressionId::Thinking
      &&visibleShapeCount(&eyes)==2&&eyes[0].height()<eyes[1].height()*.70f;
}

bool errorEntryKeepsTwoVisibleEyes(){
  AvatarEngine entry;
  if(!entry.begin())return false;
  tick(entry,400);
  entry.play(ExpressionId::Error,testClock,AvatarEngine::PlaybackMode::Once,true);
  for(unsigned elapsed=17;elapsed<=204;elapsed+=17){
    tick(entry,17);
    if(elapsed<170)continue;
    int minimumX[]={466,466},maximumX[]={-1,-1};
    for(int y=0;y<466;++y)for(int x=0;x<466;++x){
      if(M5.Display.pixels[y*466+x]==TFT_BLACK)continue;
      const unsigned side=x<233?0:1;
      minimumX[side]=std::min(minimumX[side],x);
      maximumX[side]=std::max(maximumX[side],x);
    }
    const int leftWidth=maximumX[0]-minimumX[0]+1;
    const int rightWidth=maximumX[1]-minimumX[1]+1;
    const int gap=minimumX[1]-maximumX[0]-1;
    // Protect the silhouette handover near 160 ms without freezing exact art:
    // both closed eyes remain lines, not tiny dots, with black space between.
    if(leftWidth<25||rightWidth<25||gap<12){
      printf("Error entry lost its eye silhouettes at +%u ms: widths=%d,%d gap=%d\n",elapsed,leftWidth,rightWidth,gap);
      return false;
    }
  }
  return true;
}
int main(int argc,char** argv){
  AvatarEngine a;check(a.begin(),"engine initialization");
  for(unsigned i=0;i<18;i++){
    std::string cmd=std::string("STATE ")+codexpet::kExpressions[i];auto c=codexpet::parse(cmd.data(),cmd.size());
    check(c.type==codexpet::CommandType::State&&c.value==i,"protocol expression matches enum");
  }
  for(auto base:{ExpressionId::Thinking,ExpressionId::Working,ExpressionId::Waiting}){
    a.setPersistentState(base,testClock);tick(a,10000);check(a.activeExpression()==base,"persistent animation loops");
    for(auto reaction:{ExpressionId::Success,ExpressionId::Error,ExpressionId::Stop,ExpressionId::Wake}){
      a.play(reaction,testClock,AvatarEngine::PlaybackMode::Once,true);tick(a,reactionDuration(reaction)-100);
      check(a.activeExpression()==reaction,"reaction remains visible for its specified duration");
      tick(a,350);
      check(a.activeExpression()==base&&a.baseExpression()==base,"reaction returns to current base");
      a.play(reaction,testClock,AvatarEngine::PlaybackMode::Once,true);tick(a,100);
      a.setPersistentState(ExpressionId::Waiting,testClock);
      check(a.activeExpression()==ExpressionId::Waiting,"reaction immediately interruptible");
      a.setPersistentState(base,testClock);
    }
  }
  a.setPersistentState(ExpressionId::Working,testClock);tick(a,1000);
  const auto before=allocations;tick(a,180000);check(allocations==before,"three simulated minutes without render heap allocations");
  const auto reactionsBefore=allocations;
  for(unsigned i=0;i<20;++i) for(auto reaction:{ExpressionId::Success,ExpressionId::Error,ExpressionId::Stop,ExpressionId::Wake}) {
    a.play(reaction,testClock,AvatarEngine::PlaybackMode::Once,true);tick(a,reactionDuration(reaction)+250);
  }
  check(allocations==reactionsBefore,"80 reaction cycles without render heap allocations");
  bool writingBounded=true,writingMonotone=true,writingPauses=true,writingGazeReturns=true,writingFinishes=true,writingTransport=true;
  for(unsigned cycle=0;cycle<100;++cycle){
    const unsigned start=cycle*codexpet::kWorkingPassMs;
    float previousProgress=0;
    float lastX=1000;
    for(unsigned local=0;local<codexpet::kWorkingPassMs;local+=7){
      const auto w=codexpet::sampleWriting(start+local);
      for(float value:{w.progress,w.x,w.y,w.angle,w.focus,w.ink,w.ponder,w.lookX,w.lookY,w.roll,w.leftAngle,w.rightAngle,w.blink})writingBounded&=std::isfinite(value);
      writingBounded&=w.cycle==cycle%2&&w.x>=-207&&w.x<=186&&w.y>=300&&w.y<=395
          &&w.progress>=0&&w.progress<=1.000001f&&w.focus>=0&&w.focus<=1.00001f
          &&w.ink>=0&&w.ink<=1.00001f&&w.ponder>=0&&w.ponder<=1.00001f;
      // Exclude the deliberate reset to a new line at the cycle boundary.
      writingMonotone&=w.progress+0.000002f>=previousProgress;
      previousProgress=w.progress;
      if(local>=6144&&local<=7024){
        writingTransport&=fabsf(w.angle-1.06f)<.00001f&&w.x<=lastX+.00001f;
        lastX=w.x;
      }
    }
    const unsigned pause=cycle%2?1480:1600;
    const auto shortPause=codexpet::sampleWriting(start+pause);
    const auto longPause=codexpet::sampleWriting(start+3120);
    writingPauses&=shortPause.progress>0&&shortPause.progress<longPause.progress&&longPause.progress<1
        &&shortPause.progress==codexpet::sampleWriting(start+pause+20).progress
        &&longPause.progress==codexpet::sampleWriting(start+3240).progress
        &&shortPause.y<codexpet::writingY(shortPause.progress,cycle)
        &&longPause.y<codexpet::writingY(longPause.progress,cycle);
    writingGazeReturns&=codexpet::sampleWriting(start+1000).focus>.99f&&longPause.lookY<0
        &&(cycle%2?longPause.lookX>20:longPause.lookX<-20)
        &&codexpet::sampleWriting(start+4100).focus>.99f&&codexpet::sampleWriting(start+7280).focus==0;
    const auto end=codexpet::sampleWriting(start+codexpet::kWorkingPassMs-1);
    const auto next=codexpet::sampleWriting(start+codexpet::kWorkingPassMs);
    writingFinishes&=codexpet::sampleWriting(start+5560).progress>.9999f&&end.ink==0&&next.progress==0
        &&fabsf(end.x-next.x)<.001f&&fabsf(end.y-next.y)<.001f&&fabsf(end.angle-next.angle)<.001f;
  }
  check(writingBounded,"100 writing cycles: all pen and gaze values remain finite and bounded");
  check(writingMonotone,"100 writing cycles: ink never moves backwards within a line");
  check(writingPauses,"100 writing cycles: two real internal pen pauses with a slight lift");
  check(writingGazeReturns,"100 writing cycles: gaze looks up while pondering then returns to the line");
  check(writingFinishes,"100 writing passes: ink disappears and the pen restarts at the same position without a seam");
  check(writingTransport,"100 writing passes: return moves left continuously with a fixed quill angle");
  check(workingConvergesAndErases(),"Working renders inward-leaning eyes on both passes, erases ink, and remains interruptible");
  for(unsigned i=0;i<8;i++) for(unsigned t=0;t<5100;t+=7){auto c=codexpet::sampleConfetti(i,t);if(c.visible&&(!std::isfinite(c.y)||fabsf(c.x)>330||c.y < -356||c.y>155||!std::isfinite(c.angle)))return 3;}
  check(true,"confetti trajectories stay inside the intended region");
  check(dirtyRectsEraseEveryPixel(),"all 18 expressions, tilts, swipes and interruptions match a freshly cleared framebuffer");
  check(thinkingUsesOnlyTwoEyes(),"Angry to Thinking removes isolated brows immediately and keeps two eyes throughout two loops");
  check(thinkingAlternatesItsSquint(),"Thinking alternates a clearly squinted eye in both held poses over two loops");
  check(thinkingLoopSettlesWithoutJump(),"Thinking keeps its settled silhouette across the loop boundary");
  check(thinkingHeldPosesRemainInterruptible(),"Thinking held poses interrupt immediately and a reaction restores the performance");
  check(errorEntryKeepsTwoVisibleEyes(),"Error entry keeps two separated eye silhouettes instead of shrinking to dots");
  a.invalidate(); // The framebuffer checks above used their own engines.
  if(argc>1){
    for(auto id:presentationExpressions){a.setPersistentState(ExpressionId::Idle,testClock);tick(a,400);a.play(id,testClock,AvatarEngine::PlaybackMode::Once,false);tick(a,id==ExpressionId::Thinking?1500:id==ExpressionId::Working?2800:(id==ExpressionId::Waiting?1900:(id==ExpressionId::Success?1900:(id==ExpressionId::Wake?1500:(id==ExpressionId::Sleepy?1100:550)))));M5.Display.save(std::string(argv[1])+"/"+codexpet::kExpressions[static_cast<unsigned>(id)]+".ppm");}
    if(argc>2) for(auto id:presentationExpressions) {
      a.setPersistentState(ExpressionId::Idle,testClock);tick(a,400);
      if(id==ExpressionId::Wake){a.play(ExpressionId::Sleepy,testClock,AvatarEngine::PlaybackMode::Once,false);tick(a,1350);}
      a.play(id,testClock,AvatarEngine::PlaybackMode::Once,false);
      a.invalidate();a.update(testClock);
      const unsigned duration=previewDuration(id);
      const unsigned start=testClock;
      for(unsigned elapsed=0;elapsed<duration;elapsed+=100) {
        while(testClock<start+elapsed){testClock+=10;a.update(testClock);}
        char suffix[80];std::snprintf(suffix,sizeof(suffix),"/%s-%04u.ppm",codexpet::kExpressions[static_cast<unsigned>(id)],elapsed);
        M5.Display.save(std::string(argv[1])+suffix);
      }
    }
    // Optional legacy stills let the shared eye spacing be reviewed across the
    // full catalogue without producing another nine frame sequences.
    if(argc>2)for(unsigned index=0;index<static_cast<unsigned>(ExpressionId::Count);++index){
      const auto id=static_cast<ExpressionId>(index);
      if(std::find(std::begin(presentationExpressions),std::end(presentationExpressions),id)!=std::end(presentationExpressions))continue;
      a.setPersistentState(ExpressionId::Idle,testClock);tick(a,400);
      a.play(id,testClock,AvatarEngine::PlaybackMode::Once,false);tick(a,650);
      M5.Display.save(std::string(argv[1])+"/"+codexpet::kExpressions[index]+".ppm");
    }
  }
  printf("RESULT: firmware native tests passed\n");
}
