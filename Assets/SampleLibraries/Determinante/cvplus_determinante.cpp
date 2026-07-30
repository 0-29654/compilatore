#include "cvplus_determinante.hpp"
#include <cmath>
#include <stdexcept>
#include <utility>
namespace cvplus {
double determinante(const std::vector<std::vector<double>>& input) {
    const std::size_t n = input.size();
    if (n == 0) throw std::invalid_argument("La matrice non può essere vuota");
    for (const auto& r : input) if (r.size() != n) throw std::invalid_argument("La matrice deve essere quadrata");
    auto a = input; double det = 1.0; int segno = 1;
    constexpr double eps = 1e-12;
    for (std::size_t i=0;i<n;++i) {
        std::size_t pivot=i;
        for (std::size_t r=i+1;r<n;++r) if (std::abs(a[r][i]) > std::abs(a[pivot][i])) pivot=r;
        if (std::abs(a[pivot][i]) < eps) return 0.0;
        if (pivot != i) { std::swap(a[pivot],a[i]); segno=-segno; }
        const double p=a[i][i]; det*=p;
        for (std::size_t r=i+1;r<n;++r) {
            const double f=a[r][i]/p;
            for (std::size_t c=i+1;c<n;++c) a[r][c]-=f*a[i][c];
        }
    }
    return det*segno;
}
}
