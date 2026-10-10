# Conditional logistic regression: the StatsDirect help example (Hosmer and Lemeshow
# 1989, low birth weight in 56 matched pairs) in R. The data are the test workbook's
# Regression worksheet columns PAIRID, LBWT, RACE, LWT, SMOKE, PTD, HT and UI.
lowbwt <- data.frame(
  PAIRID = c(1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11,
    11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17, 18, 18, 19, 19, 20, 20,
    21, 21, 22, 22, 23, 23, 24, 24, 25, 25, 26, 26, 27, 27, 28, 28, 29, 29, 30,
    30, 31, 31, 32, 32, 33, 33, 34, 34, 35, 35, 36, 36, 37, 37, 38, 38, 39, 39,
    40, 40, 41, 41, 42, 42, 43, 43, 44, 44, 45, 45, 46, 46, 47, 47, 48, 48, 49,
    49, 50, 50, 51, 51, 52, 52, 53, 53, 54, 54, 55, 55, 56, 56),
  LBWT = c(0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0,
    1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1,
    0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0,
    1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1,
    0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1),
  RACE = c("white", "other", "black", "other", "other", "other", "other",
    "other", "white", "white", "black", "white", "black", "black", "other",
    "black", "white", "other", "white", "black", "other", "white", "other",
    "white", "white", "white", "other", "white", "other", "other", "other",
    "black", "white", "other", "black", "other", "other", "white", "other",
    "black", "white", "other", "white", "white", "other", "black", "black",
    "other", "white", "other", "white", "white", "other", "white", "black",
    "white", "black", "other", "other", "black", "other", "other", "other",
    "white", "white", "other", "white", "black", "white", "other", "other",
    "white", "other", "white", "other", "black", "white", "other", "other",
    "other", "white", "other", "black", "white", "white", "other", "black",
    "other", "white", "white", "black", "other", "other", "other", "other",
    "white", "white", "black", "other", "other", "other", "white", "white",
    "white", "white", "white", "white", "white", "other", "white", "white",
    "black"),
  LWT = c(135, 101, 98, 115, 95, 130, 103, 130, 122, 110, 113, 120, 113, 120,
    119, 142, 100, 148, 90, 110, 150, 91, 115, 102, 235, 112, 120, 150, 103,
    125, 169, 120, 141, 80, 121, 109, 127, 121, 120, 122, 158, 105, 108, 165,
    124, 200, 185, 103, 160, 100, 115, 130, 95, 130, 158, 130, 130, 97, 128,
    187, 119, 120, 115, 110, 190, 94, 90, 128, 115, 132, 110, 155, 115, 138,
    110, 105, 118, 105, 120, 85, 155, 115, 125, 92, 140, 89, 241, 105, 113, 117,
    168, 96, 133, 154, 160, 190, 124, 130, 120, 120, 130, 95, 135, 130, 95, 142,
    215, 102, 121, 105, 170, 187),
  SMOKE = c(0, 1, 0, 0, 0, 0, 0, 1, 1, 1, 0, 1, 0, 0, 0, 0, 1, 0, 1, 1, 0, 1, 0,
    0, 1, 1, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0, 0, 1, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0,
    0, 0, 0, 1, 0, 1, 0, 1, 0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 0, 0, 0, 0, 1, 0,
    0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 1, 1, 0, 1, 0, 0, 1, 1, 0,
    0, 1, 0, 1, 0, 0, 1, 1, 1, 1, 0, 1, 0, 1),
  PTD = c(0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0,
    0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 0, 1, 0,
    0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 0, 0, 1, 1, 0, 0, 0, 0,
    0, 1, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 1, 0),
  HT = c(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0,
    1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0,
    0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0,
    0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1),
  UI = c(0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0,
    0, 1, 1, 0, 0, 1, 1, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0,
    1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0))
lowbwt$black <- as.numeric(lowbwt$RACE == "black")   # the dummy variable RACE (b)

# R's conditional logistic regression is in the survival package, which comes with
# R: the strata are the matched pairs. The last place of the widest odds ratio
# limits depends on where the iterations stop, in R as in StatsDirect
library(survival)
fit <- clogit(LBWT ~ black + SMOKE + HT + UI + PTD + LWT + strata(PAIRID),
              data = lowbwt)
print(summary(fit))

# The report's lines to 6 places. The deviance is -2 times the log likelihood, the
# likelihood ratio chi-square compares it with the model of no predictors (whose
# log likelihood is -56 log 2 for 56 pairs), and McFadden's pseudo R-square is 1
# minus the ratio of the two log likelihoods
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
ll <- fit$loglik[2]
ll0 <- fit$loglik[1]
cat("Deviance (-2 log likelihood) =", six(-2 * ll), "\n")
cat("Deviance (likelihood ratio) chi-square =", six(2 * (ll - ll0)), " ",
    pv(pchisq(2 * (ll - ll0), 6, lower.tail = FALSE)), "\n")
cat("Pseudo (McFadden) R-square =", six(1 - ll / ll0), "\n")
s <- summary(fit)$coefficients
lab <- c("RACE (b)", "SMOKE", "HT", "UI", "PTD", "LWT")
for (i in 1:6) {
  cat(lab[i], six(s[i, "coef"]), six(s[i, "se(coef)"]), " z =", six(s[i, "z"]), " ",
      pv(s[i, "Pr(>|z|)"]), "\n")
}
ci <- summary(fit)$conf.int
for (i in 1:6) {
  cat(lab[i], six(ci[i, "exp(coef)"]), six(ci[i, "lower .95"]), "to",
      six(ci[i, "upper .95"]), "\n")
}
