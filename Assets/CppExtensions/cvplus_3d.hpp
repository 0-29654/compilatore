#pragma once
// Compatibilita con gli esempi precedenti di CV+ Grafici 3D.
#include <mate.hpp>
namespace cvplus3d {
inline void grafico(const std::string& formula, double xmin=NAN, double xmax=NAN, double ymin=NAN, double ymax=NAN, const std::string& titolo="") {
    mate::grafico3d(formula, xmin, xmax, ymin, ymax, titolo);
}
template<class F>
void grafico(F funzione, double xmin=-4, double xmax=4, double ymin=-4, double ymax=4, const std::string& titolo="z = f(x,y)", int campioni=55) {
    (void)campioni;
    // Compatibilita lambda: campiona e genera una superficie usando un file HTML dedicato non disponibile nel nuovo parser.
    // Per i nuovi esercizi usare mate::grafico3d("z = ...").
    const int N=55; std::vector<double>Z; Z.reserve(N*N); double zmin=1e300,zmax=-1e300;
    for(int j=0;j<N;j++){double y=ymin+(ymax-ymin)*j/(N-1.0);for(int i=0;i<N;i++){double x=xmin+(xmax-xmin)*i/(N-1.0);double z=funzione(x,y);if(!std::isfinite(z))z=NAN;Z.push_back(z);if(std::isfinite(z)){zmin=std::min(zmin,z);zmax=std::max(zmax,z);}}}
    std::ofstream o("cvplus_grafico_3d_compat.html");o<<mate::html_head(titolo)<<"<p style='padding:20px'>Questo esempio usa la sintassi precedente. Per grafici completi usa <b>mate::grafico3d(\"z = ...\")</b>.</p></body></html>";o.close();mate::open_html("cvplus_grafico_3d_compat.html");
}
}
