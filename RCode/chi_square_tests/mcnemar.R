# McNemar and exact (Liddell) test for matched pairs: the StatsDirect help example
# (Armitage and Berry 1994, p. 127, fifty sputum specimens cultured on two media) in R
counts <- matrix(c(20, 12, 2, 16), 2, byrow = TRUE,
                 dimnames = list("Medium A" = c("Growth", "No growth"),
                                 "Medium B" = c("Growth", "No growth")))
print(counts)
b <- counts[1, 2]     # discordant pairs: growth on medium A only
cc <- counts[2, 1]    # growth on medium B only (the cell the table above calls c)

# R's standard McNemar test uses only the discordant pairs b and c. Its default,
# correct = TRUE, applies Yates' continuity correction, as the report's second
# chi-square does; correct = FALSE gives the uncorrected chi-square. When b = c, R
# prints 0 for both chi-squares (P = 1).
print(mcnemar.test(counts, correct = FALSE))
print(mcnemar.test(counts))

# Liddell's exact test: under the null hypothesis b is binomial from b + c with
# probability 1/2, so binom.test gives the two sided P and the exact
# (Clopper-Pearson) 95% confidence interval for the proportion b/(b + c)
exact <- binom.test(b, b + cc)
print(exact)

# The figures as the report prints them, to 6 places (P values to 4)
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
chi <- mcnemar.test(counts, correct = FALSE)
yates <- mcnemar.test(counts)
cat("Uncorrected Chi-square =", six(chi$statistic), "(1 DF)  ", pv(chi$p.value), "\n")
cat("Yates' continuity corrected Chi-square =", six(yates$statistic), "(1 DF)  ",
    pv(yates$p.value), "\n")

# R' = b/c is the proportion b/(b + c) expressed as odds, p/(1 - p), so its exact
# interval is the binomial interval transformed the same way. It equals the
# interval the F quantiles above give when b and c are both positive.
odds <- exact$conf.int / (1 - exact$conf.int)
cat("Point estimate of relative risk (R') =", six(b / cc), "\n")
cat("Exact 95% confidence interval =", six(odds[1]), "to", six(odds[2]), "\n")
lower <- b / ((cc + 1) * qf(0.975, 2 * (cc + 1), 2 * b))
upper <- (b + 1) * qf(0.975, 2 * (b + 1), 2 * cc) / cc
cat("  (from the F quantiles:", six(lower), "to", six(upper), ")\n")

# F = b/(c + 1), with b the larger of the two discordant counts, has 2(c + 1) and 2b
# degrees of freedom; twice its upper tail area is the binomial two sided P above
r <- max(b, cc)
s <- min(b, cc)
fstat <- r / (s + 1)
cat("F =", six(fstat), "\n")
cat("Two sided", pv(min(1, 2 * pf(fstat, 2 * (s + 1), 2 * r, lower.tail = FALSE))),
    "\n")
