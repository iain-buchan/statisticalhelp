# Two sample Smirnov test: the StatsDirect help example (Conover 1999) in R
xi <- c(7.6, 8.4, 8.6, 8.7, 9.3, 9.9, 10.1, 10.6, 11.2)
yi <- c(5.2, 5.7, 5.9, 6.5, 6.8, 8.2, 9.1, 9.8, 10.8, 11.3, 11.5, 12.3, 12.5,
        13.4, 14.6)

# R's standard test, which it calls the two-sample Kolmogorov-Smirnov test. D is
# the largest vertical distance between the two sample distribution functions.
# StatsDirect's P is always exact. R's is exact by default when the product of
# the sample sizes is less than 10000; exact = TRUE asks for it at any size.
two <- ks.test(xi, yi, exact = TRUE)
print(two)
cat(sprintf("Two sided test:          D = %g   P = %.4f\n",
            two$statistic, two$p.value))

# One sided tests. "greater" asks whether the distribution function of xi lies
# above that of yi, which is what happens when xi is shifted left of yi (its
# values tend to be smaller); "less" asks whether xi is shifted right of yi.
left <- ks.test(xi, yi, alternative = "greater", exact = TRUE)
right <- ks.test(xi, yi, alternative = "less", exact = TRUE)
cat(sprintf("Xi shifted left of Yi:   D = %g   P = %.4f\n",
            left$statistic, left$p.value))
cat(sprintf("Xi shifted right of Yi:  D = %g   P = %.4f\n",
            right$statistic, right$p.value))

# The three D statistics from the sample distribution functions
v <- sort(c(xi, yi))
fx <- ecdf(xi)(v)                         # proportion of xi at or below each value
fy <- ecdf(yi)(v)
cat("D =", max(abs(fx - fy)), "  D (left) =", max(fx - fy),
    "  D (right) =", round(max(fy - fx), 6), "\n")
