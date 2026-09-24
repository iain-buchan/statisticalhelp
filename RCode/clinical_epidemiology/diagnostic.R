# Diagnostic test 2 by 2 table: the StatsDirect help example (a hypothetical serum
# marker of disease, positive at 100 units or more: 431 diseased and 30 disease-free
# patients tested positive, 29 and 116 tested negative) in R
tp <- 431   # a: test positive, disease present
fp <- 30    # b: test positive, disease absent
fn <- 29    # c: test negative, disease present
tn <- 116   # d: test negative, disease absent
n <- tp + fp + fn + tn
counts <- matrix(c(tp, fp, fn, tn), 2, byrow = TRUE,
                 dimnames = list(Test = c("Positive", "Negative"),
                                 Disease = c("Present", "Absent")))
print(addmargins(counts))

# Each rate in the report is a binomial proportion with the exact (Clopper-Pearson)
# confidence interval that binom.test gives; the prevalence, for instance:
print(binom.test(tp + fn, n))

# The report prints each rate as a proportion and as a percentage
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pc <- function(x) {
  paste0(formatC(100 * x, digits = 2, format = "f", drop0trailing = TRUE), "%")
}
rate <- function(x, n) {
  ci <- binom.test(x, n)$conf.int
  paste0(six(x / n), " (", six(ci[1]), " to ", six(ci[2]), "), ", pc(x / n), " (",
         pc(ci[1]), " to ", pc(ci[2]), ")")
}
prevalence <- (tp + fn) / n
cat("Prevalence (pre-test likelihood of disease)\n")
cat(rate(tp + fn, n), "\n")

# The change, in braces, is from the pre-test to the post-test likelihood, in whole
# percentages (halves round to even, as the program does; the pre-test rate is
# evaluated as the program evaluates it, so that both round alike)
ppv <- tp / (tp + fp)
cat("Predictive value of +ve test (post-test likelihood of disease)\n")
cat(rate(tp, tp + fp), ", {change = ", round(100 * ppv) - round(100 * prevalence),
    "%}\n", sep = "")
npv <- tn / (tn + fn)
cat("Predictive values of -ve test\n")
cat("(post-test likelihood of no disease)\n")
cat(rate(tn, tn + fn), ", {change = ", round(100 * npv) - round((fp + tn) / n * 100),
    "%}\n", sep = "")
# 1 - npv, whose exact interval is that of the false negatives among the test negatives
cat("(post-test disease likelihood despite -ve test)\n")
cat(rate(fn, tn + fn), ", {change = ",
    round(100 * (1 - npv)) - round(100 * prevalence), "%}\n", sep = "")
sens <- tp / (tp + fn)
cat("Sensitivity (true positive rate)\n")
cat(rate(tp, tp + fn), "\n")
spec <- tn / (fp + tn)
cat("Specificity (true negative rate)\n")
cat(rate(tn, fp + tn), "\n")

# A likelihood ratio is a ratio of two binomial proportions: the positive rate among
# the diseased over the positive rate among the disease-free, and the negative rate
# among the diseased over the negative rate among the disease-free. Its interval is
# Koopman's (1984) score interval: the ratios at which the score chi-square, with the
# proportions estimated under the constraint that they have that ratio, reaches
# the 95% critical value
koopman <- function(x1, n1, x0, n0, level = 0.95) {
  chi2 <- function(theta) {
    A <- (n0 + n1) * theta
    B <- -((x0 + n1) * theta + x1 + n0)
    p0 <- (-B - sqrt(B^2 - 4 * A * (x0 + x1))) / (2 * A)  # constrained estimate
    p1 <- theta * p0
    (x1 - n1 * p1)^2 / (n1 * p1 * (1 - p1)) *
      (1 + n1 * (theta - p1) / (n0 * (1 - p1)))
  }
  est <- (x1 / n1) / (x0 / n0)                             # both counts non-zero here
  f <- function(lt) chi2(exp(lt)) - qchisq(level, 1)
  lower <- exp(uniroot(f, c(log(est) - 20, log(est)), tol = 1e-12)$root)
  upper <- exp(uniroot(f, c(log(est), log(est) + 20), tol = 1e-12)$root)
  c(est, lower, upper)
}
cat("Likelihood Ratio\n")
lr_pos <- koopman(tp, tp + fn, fp, fp + tn)
cat("LR (positive test) = ", six(lr_pos[1]), " (", six(lr_pos[2]), " to ",
    six(lr_pos[3]), ")\n", sep = "")
lr_neg <- koopman(fn, tp + fn, tn, fp + tn)
cat("LR (negative test) = ", six(lr_neg[1]), " (", six(lr_neg[2]), " to ",
    six(lr_neg[3]), ")\n", sep = "")

# The diagnostic odds ratio: observed, then the conditional maximum likelihood
# estimate with its exact interval, which fisher.test gives
cat("Diagnostic Odds Ratio\n")
cat("Observed odds ratio =", six(tp * tn / (fp * fn)), "\n")
print(fisher.test(counts))

# fisher.test's estimate and limits agree with the report's to about four significant
# figures. Solving the same equations closely reproduces the report: given the
# margins, the true positive count follows the non-central hypergeometric
# distribution with the odds ratio as its parameter; the estimate makes its mean the
# observed count, and each limit puts a tail probability of 2.5% at the observed count
m1 <- tp + fp
n1 <- tp + fn
x <- max(0, m1 - (n - n1)):min(m1, n1)
logd <- dhyper(x, n1, n - n1, m1, log = TRUE)
dens <- function(log_or) {
  w <- exp(logd + x * log_or - max(logd + x * log_or))
  w / sum(w)
}
solve_or <- function(f) exp(uniroot(f, c(-40, 40), tol = 1e-13)$root)
cmle <- solve_or(function(lo) sum(x * dens(lo)) - tp)
cmle_lower <- solve_or(function(lo) sum(dens(lo)[x >= tp]) - 0.025)
cmle_upper <- solve_or(function(lo) sum(dens(lo)[x <= tp]) - 0.025)
cat("Conditional maximum likelihood estimate = ", six(cmle), " (", six(cmle_lower),
    " to ", six(cmle_upper), ")\n", sep = "")
