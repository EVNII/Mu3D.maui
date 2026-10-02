// Independent macOS oracle. Build/run commands and output schema are in README.md.
#include <ColorSync/ColorSync.h>
#include <CoreGraphics/CoreGraphics.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static ColorSyncTransformRef make_transform(ColorSyncProfileRef a,ColorSyncProfileRef b,CFStringRef intent){
 const void*keys[]={kColorSyncProfile,kColorSyncRenderingIntent,kColorSyncTransformTag};
 const void*iv[]={a,intent,kColorSyncTransformDeviceToPCS};const void*ov[]={b,intent,kColorSyncTransformPCSToDevice};
 CFDictionaryRef ds[]={CFDictionaryCreate(NULL,keys,iv,3,&kCFTypeDictionaryKeyCallBacks,&kCFTypeDictionaryValueCallBacks),CFDictionaryCreate(NULL,keys,ov,3,&kCFTypeDictionaryKeyCallBacks,&kCFTypeDictionaryValueCallBacks)};
 CFArrayRef seq=CFArrayCreate(NULL,(const void**)ds,2,&kCFTypeArrayCallBacks);ColorSyncTransformRef t=ColorSyncTransformCreate(seq,NULL);
 CFRelease(seq);CFRelease(ds[0]);CFRelease(ds[1]);return t;
}
int main(int argc,char**argv){
 ColorSyncProfileRef rp=ColorSyncProfileCreateWithName(kColorSyncGenericXYZProfile);
 CFStringRef intents[]={kColorSyncRenderingIntentPerceptual,kColorSyncRenderingIntentRelative,kColorSyncRenderingIntentSaturation,kColorSyncRenderingIntentAbsolute};
 for(int f=1;f<argc;f++){
  FILE*file=fopen(argv[f],"rb");if(!file)return 2;fseek(file,0,SEEK_END);long len=ftell(file);rewind(file);unsigned char*b=malloc(len);if(fread(b,1,len,file)!=(size_t)len)return 2;fclose(file);
  CFDataRef d=CFDataCreate(NULL,b,len);ColorSyncProfileRef p=ColorSyncProfileCreate(d,NULL);if(!p)return 3;
  const char*name=strrchr(argv[f],'/');name=name?name+1:argv[f];
  for(int intent=0;intent<4;intent++)for(int direction=0;direction<2;direction++){
   ColorSyncTransformRef t=make_transform(direction?rp:p,direction?p:rp,intents[intent]);if(!t)return 4;
   for(int sample=0;sample<32;sample++){
    float input[4]={0},output[4]={0};int ni=direction?3:4,no=direction?4:3;
    for(int c=0;c<ni;c++)input[c]=((sample*(c*8+7)+c*17)%101)/100.0f;
    if(direction){float r=.05f+.9f*input[0],g=.05f+.9f*input[1],b=.05f+.9f*input[2];
     input[0]=.43606574f*r+.38515147f*g+.14307842f*b;
     input[1]=.22249319f*r+.71688705f*g+.06061979f*b;
     input[2]=.013923905f*r+.09708128f*g+.71409935f*b;}
    if(!ColorSyncTransformConvert(t,1,1,output,kColorSync32BitFloat,kColorSyncAlphaNone,no*4,input,kColorSync32BitFloat,kColorSyncAlphaNone,ni*4,NULL))return 5;
    printf("%s,%d,%d",name,intent,direction);for(int c=0;c<4;c++)printf(",%.9g",input[c]);for(int c=0;c<4;c++)printf(",%.9g",output[c]);puts("");
   }
   CFRelease(t);
  }
  CFRelease(p);CFRelease(d);free(b);
 }
 CFRelease(rp);return 0;
}
