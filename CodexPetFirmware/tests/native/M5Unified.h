// Test-only Arduino/M5 adapter. Runs the production engine on a software framebuffer.
#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <cctype>
#include <string>
#include <fstream>
constexpr uint16_t TFT_BLACK=0, TFT_WHITE=0xffff, TFT_LIGHTGREY=0xc618;
inline uint32_t testClock=0;
inline uint32_t millis(){return testClock;}
inline uint32_t micros(){return testClock*1000;}
inline long random(long low,long high){return low+(high-low)/2;}
struct String : std::string {
  using std::string::string;
  void trim(){auto a=find_first_not_of(" \t\r\n"),b=find_last_not_of(" \t\r\n"); if(a==npos) clear(); else assign(substr(a,b-a+1));}
  void toLowerCase(){for(char& c:*this)c=static_cast<char>(std::tolower(c));}
  bool startsWith(const char* x)const{return rfind(x,0)==0;}
  void remove(size_t start,size_t count){erase(start,count);}
};
struct TestSerial { template<class... A> void printf(const char*,A...){} };
inline TestSerial Serial;
struct TestDisplay {
  std::array<uint16_t,466*466> pixels{};
  int width()const{return 466;} int height()const{return 466;}
  void startWrite(){} void endWrite(){}
  uint16_t color565(unsigned r,unsigned g,unsigned b){return ((r>>3)<<11)|((g>>2)<<5)|(b>>3);}
  void pixel(int x,int y,uint16_t c){if(x>=0&&x<466&&y>=0&&y<466)pixels[y*466+x]=c;}
  void fillScreen(uint16_t c){pixels.fill(c);}
  void fillRect(int x,int y,int w,int h,uint16_t c){for(int j=std::max(0,y);j<std::min(466,y+h);j++)for(int i=std::max(0,x);i<std::min(466,x+w);i++)pixel(i,j,c);}
  void fillCircle(int x,int y,int r,uint16_t c){for(int j=-r;j<=r;j++)for(int i=-r;i<=r;i++)if(i*i+j*j<=r*r)pixel(x+i,y+j,c);}
  void fillRoundRect(int x,int y,int w,int h,int r,uint16_t c){r=std::min(r,std::min(w,h)/2);fillRect(x+r,y,w-2*r,h,c);fillRect(x,y+r,w,h-2*r,c);for(int a:{x+r,x+w-r-1})for(int b:{y+r,y+h-r-1})fillCircle(a,b,r,c);}
  void fillTriangle(int x1,int y1,int x2,int y2,int x3,int y3,uint16_t c){
    auto edge=[](int ax,int ay,int bx,int by,int x,int y){return (x-ax)*(by-ay)-(y-ay)*(bx-ax);};
    for(int y=std::max(0,std::min({y1,y2,y3}));y<=std::min(465,std::max({y1,y2,y3}));y++)
      for(int x=std::max(0,std::min({x1,x2,x3}));x<=std::min(465,std::max({x1,x2,x3}));x++){
        int a=edge(x1,y1,x2,y2,x,y),b=edge(x2,y2,x3,y3,x,y),d=edge(x3,y3,x1,y1,x,y);
        if((a>=0&&b>=0&&d>=0)||(a<=0&&b<=0&&d<=0))pixel(x,y,c);
      }
  }
  void drawLine(int x0,int y0,int x1,int y1,uint16_t c){int dx=abs(x1-x0),sx=x0<x1?1:-1,dy=-abs(y1-y0),sy=y0<y1?1:-1,e=dx+dy;for(;;){pixel(x0,y0,c);if(x0==x1&&y0==y1)break;int t=2*e;if(t>=dy){e+=dy;x0+=sx;}if(t<=dx){e+=dx;y0+=sy;}}}
  void drawWideLine(int x0,int y0,int x1,int y1,float w,uint16_t c){int n=std::max(abs(x1-x0),abs(y1-y0));for(int i=0;i<=n;i++){float t=n?float(i)/n:0;fillCircle(int(x0+(x1-x0)*t),int(y0+(y1-y0)*t),std::max(1,int(w/2)),c);}}
  void fillEllipseArc(int x,int y,int rx,int ix,int ry,int iy,float,float,uint16_t c){
    for(int j=-ry;j<=0;j++)for(int i=-rx;i<=rx;i++){
      float outer=float(i*i)/std::max(1,rx*rx)+float(j*j)/std::max(1,ry*ry);
      float inner=float(i*i)/std::max(1,ix*ix)+float(j*j)/std::max(1,iy*iy);
      if(outer<=1&&(inner>=1||ix==0||iy==0))pixel(x+i,y+j,c);
    }
  }
  void save(const std::string& file){std::ofstream out(file,std::ios::binary);out<<"P6\n466 466\n255\n";for(auto p:pixels){char rgb[]={char(((p>>11)&31)*255/31),char(((p>>5)&63)*255/63),char((p&31)*255/31)};out.write(rgb,3);}}
};
struct TestM5 {TestDisplay Display;};
inline TestM5 M5;
