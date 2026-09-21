# Kendall's rank correlation: the StatsDirect help example (Armitage and Berry
# 1994, p. 466) in R
career     <- c(4, 10, 3, 1, 9, 2, 6, 7, 8, 5)
psychology <- c(5, 8, 6, 2, 10, 3, 9, 4, 7, 1)
n <- length(career)

# R's standard test. Its T is the number of concordant pairs (34). Without ties
# and with fewer than 50 pairs its P is exact, as is StatsDirect's.
# Use alternative = "greater" for the upper side P and "less" for the lower.
print(cor.test(career, psychology, method = "kendall"))

# The rest of this code is for data without ties. With ties R's P is no longer
# exact, tau b replaces tau, and the variance of the score needs an adjustment.
stopifnot(!anyDuplicated(career), !anyDuplicated(psychology))
upper <- cor.test(career, psychology, method = "kendall",
                  alternative = "greater")$p.value
lower <- cor.test(career, psychology, method = "kendall",
                  alternative = "less")$p.value
cat("Exact test:  Upper side P =", round(upper, 4), "  Lower side P =",
    round(lower, 4), "  Two sided P =", round(min(1, 2 * min(upper, lower)), 4),
    "\n")

# Concordant and discordant pairs, Kendall's score and tau
q <- sign(outer(career, career, "-")) * sign(outer(psychology, psychology, "-"))
concordant <- sum(q[upper.tri(q)] > 0)
discordant <- sum(q[upper.tri(q)] < 0)
score <- concordant - discordant
se <- sqrt(n * (n - 1) * (2 * n + 5) / 18)     # standard error of the score
tau <- score / (n * (n - 1) / 2)
tied <- n * (n - 1) / 2 - concordant - discordant
cat("Concordant pairs =", concordant, "  Discordant pairs =", discordant,
    "  Tied pairs =", tied, "\n")
cat(sprintf("Kendall's score = %d   (standard error = %.5f)\n", score, se))
cat(sprintf("Gamma = %.6f\n", score / (concordant + discordant)))

# Approximate 95% confidence interval for tau: the method of Samara and Randles
# (1988) as given by Hollander and Wolfe (1999)
ci <- rowSums(q)                                # each observation's own score
v <- 2 / (n * (n - 1)) *
  (2 * (n - 2) / (n * (n - 1)^2) * sum((ci - mean(ci))^2) + 1 - tau^2)
limits <- tau + c(-1, 1) * qnorm(0.975) * sqrt(v)
cat(sprintf("Kendall's tau = %.6f   Approximate 95%% CI = %.6f to %.6f\n",
            tau, limits[1], limits[2]))

# Approximate tests from the normal distribution, without and with a continuity
# correction (StatsDirect advises the exact test for a sample as small as this)
for (cc in c(0, 1)) {
  z <- (score - cc * sign(score)) / se
  cat(sprintf("z = %.6f   Upper side P = %.4f   Lower side P = %.4f   %s%.4f\n",
              z, 1 - pnorm(z), pnorm(z), "Two sided P = ", 2 * pnorm(-abs(z))))
}
