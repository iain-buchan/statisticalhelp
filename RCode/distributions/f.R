# F (variance ratio) distribution: the StatsDirect help illustration (the upper tail
# P of the F test example, a 5% critical value, and the link with Student's t) in R

# The dialog shows tail areas to 15 decimal places and F to 15 significant figures
# (with a thousands separator above 999); p15() and x15() print R's values the same
# way. For ordinary degrees of freedom the program's integration and R's agree to
# about 14 figures, so the last digit can differ.
p15 <- function(p) formatC(p, digits = 15, format = "f", drop0trailing = TRUE)
x15 <- function(x) formatC(x, digits = 15, format = "g", drop0trailing = TRUE)

# 1. Upper tail P for F = 6.944952 on 6 and 8 degrees of freedom (the F test
# example): pf() gives the lower tail unless told otherwise
p <- pf(6.944952, df1 = 6, df2 = 8, lower.tail = FALSE)
cat("P(F 6.944952, dfn 6, dfd 8) =", p15(p), "upper\n")
cat("To four decimal places, the F test's upper side P =",
    formatC(p, digits = 4, format = "f"), "\n")

# The same by the beta transformation given in the topic: the upper tail of F
# is the lower tail of a beta variable at dfd / (dfd + dfn F)
pb <- pbeta(8 / (8 + 6 * 6.944952), shape1 = 8 / 2, shape2 = 6 / 2)
cat("Via the beta distribution:", p15(pb), "\n")

# 2. The inverse: the F with 5% in its upper tail (the 5% critical value) for
# 6 and 8 degrees of freedom
f <- qf(0.05, df1 = 6, df2 = 8, lower.tail = FALSE)
cat("F(upper P 0.05, dfn 6, dfd 8) =", x15(f), "\n")

# 3. With one numerator degree of freedom F is the square of Student's t: the
# 5% point of F on 1 and 20 degrees of freedom is the square of the two sided
# 5% point of t on 20 degrees of freedom (2.5% in each tail)
f120 <- qf(0.05, df1 = 1, df2 = 20, lower.tail = FALSE)
t20 <- qt(0.975, df = 20)
cat("F(upper P 0.05, dfn 1, dfd 20) =", x15(f120), "\n")
cat("Student's t (two sided 5% point, df 20) =", x15(t20), "  squared =",
    x15(t20^2), "\n")
