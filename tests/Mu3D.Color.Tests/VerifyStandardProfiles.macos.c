#include <ColorSync/ColorSync.h>
#include <CoreGraphics/CoreGraphics.h>
#include <stdio.h>
#include <stdlib.h>
#include <math.h>
#include <string.h>
// Independent native ICC oracle. Export profiles with MU3D_ICC_TEST_OUTPUT, then
// compile with clang -framework ColorSync -framework CoreGraphics -framework CoreFoundation.
// Pass the five exported .icc files as arguments. No window or UI is created.
int main(int argc,char**argv){
 CGColorSpaceRef out=CGColorSpaceCreateWithName(kCGColorSpaceExtendedLinearSRGB);
 CFDataRef od=CGColorSpaceCopyICCData(out); ColorSyncProfileRef op=ColorSyncProfileCreate(od,NULL);
 for(int n=1;n<argc;n++){
 FILE*f=fopen(argv[n],"rb");fseek(f,0,SEEK_END);long len=ftell(f);rewind(f);unsigned char*b=malloc(len);fread(b,1,len,f);fclose(f);
 CFDataRef d=CFDataCreate(NULL,b,len);ColorSyncProfileRef p=ColorSyncProfileCreate(d,NULL);
 const void*keys[]={kColorSyncProfile,kColorSyncRenderingIntent,kColorSyncTransformTag};
 const void*iv[]={p,kColorSyncRenderingIntentRelative,kColorSyncTransformDeviceToPCS};
 const void*ov[]={op,kColorSyncRenderingIntentRelative,kColorSyncTransformPCSToDevice};
 CFDictionaryRef ds[]={CFDictionaryCreate(NULL,keys,iv,3,&kCFTypeDictionaryKeyCallBacks,&kCFTypeDictionaryValueCallBacks),CFDictionaryCreate(NULL,keys,ov,3,&kCFTypeDictionaryKeyCallBacks,&kCFTypeDictionaryValueCallBacks)};
 CFArrayRef seq=CFArrayCreate(NULL,(const void**)ds,2,&kCFTypeArrayCallBacks);ColorSyncTransformRef t=ColorSyncTransformCreate(seq,NULL);
 float src[]={.5,.5,.5},dst[3];if(!t||!ColorSyncTransformConvert(t,1,1,dst,kColorSync32BitFloat,kColorSyncAlphaNone,12,src,kColorSync32BitFloat,kColorSyncAlphaNone,12,NULL))return 2;
 float expected = strstr(argv[n],"AdobeRgb") ? .21775553f : strstr(argv[n],"ProPhotoRgb") ? .28717459f : strstr(argv[n],"Rec2020") ? .25971944f : .21404114f;
 for (int i=0;i<3;i++) if(!isfinite(dst[i]) || fabsf(dst[i]-expected)>.0003f) return 3;
 printf("%s: ColorSync midpoint passed\n",argv[n]);
 CFRelease(t);CFRelease(seq);CFRelease(ds[0]);CFRelease(ds[1]);CFRelease(p);CFRelease(d);free(b);
 }
}
