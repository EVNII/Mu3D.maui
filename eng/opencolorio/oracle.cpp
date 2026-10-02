#include <OpenColorIO/OpenColorIO.h>
#include <iomanip>
#include <iostream>
namespace OCIO = OCIO_NAMESPACE;
int main(int argc,char** argv) {
    try {
        if(argc!=4 && argc!=5) { std::cerr<<"usage: mu3d_ocio_oracle config.ocio source destination | config.ocio source display view\n"; return 2; }
        auto config=OCIO::Config::CreateFromFile(argv[1]);
        OCIO::ConstProcessorRcPtr transform;
        if(argc==4) transform=config->getProcessor(argv[2],argv[3]);
        else {
            auto display=OCIO::DisplayViewTransform::Create();
            display->setSrc(argv[2]); display->setDisplay(argv[3]); display->setView(argv[4]);
            transform=config->getProcessor(display);
        }
        auto processor=transform->getOptimizedCPUProcessor(OCIO::OPTIMIZATION_LOSSLESS);
        float rgb[3]; std::cout<<std::setprecision(9);
        while(std::cin>>rgb[0]>>rgb[1]>>rgb[2]) {
            processor->applyRGB(rgb); std::cout<<rgb[0]<<' '<<rgb[1]<<' '<<rgb[2]<<'\n';
        }
        return 0;
    } catch(const std::exception& e) { std::cerr<<e.what()<<'\n'; return 1; }
}
