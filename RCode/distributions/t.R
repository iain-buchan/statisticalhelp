# Student's t distribution: the StatsDirect help illustration (tail areas for t = 2
# with 10 degrees of freedom and for t = -2.5 with 15, then the t with an upper tail
# of 0.025 for 10 degrees of freedom, the 5% two sided critical value) in R

# StatsDirect's calculator shows tail areas to 15 decimal places and t to 15
# significant figures; these helpers print R's values the same way.
p15 <- function(p) formatC(p, digits = 15, format = "f", drop0trailing = TRUE)
t15 <- function(x) formatC(x, digits = 15, format = "g", drop0trailing = TRUE)

# Tail areas from t: pt() gives the lower tail P(T <= t) by default, and the upper
# tail P(T > t) with lower.tail = FALSE. The two sided P is twice the smaller tail.
tails <- function(t, df) {
  upper <- pt(t, df, lower.tail = FALSE)
  lower <- pt(t, df)
  cat("P(t ", t, ", df ", df, ") = ", p15(upper), " upper, ", p15(lower), " lower, ",
      p15(2 * min(upper, lower)), " two sided\n", sep = "")
}
tails(2, 10)
tails(-2.5, 15)      # a negative t: the distribution is symmetric, so the tails swap

# t from a tail area (the calculator's Invert): qt() gives the t below which a given
# area lies, so the t with 0.025 above it (0.05 in the two tails together) is
cat("t(upper P 0.025, df 10) =", t15(qt(0.025, 10, lower.tail = FALSE)), "\n")
# which is the same as qt(0.975, 10)
