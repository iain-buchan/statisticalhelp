# Cox (proportional hazards) regression: the StatsDirect help example (Armitage and
# Berry 1994, survival in days of lymphoma patients by stage of disease; the test
# workbook's Survival worksheet columns Stage group, Time and Censor) in R
library(survival)
stage <- c(rep(1, 19), rep(2, 61))
time <- c(6, 19, 32, 42, 42, 43, 94, 126, 169, 207, 211, 227, 253, 255, 270, 310,
          316, 335, 346, 4, 6, 10, 11, 11, 11, 13, 17, 20, 20, 21, 22, 24, 24, 29, 30,
          30, 31, 33, 34, 35, 39, 40, 41, 43, 45, 46, 50, 56, 61, 61, 63, 68, 82, 85,
          88, 89, 90, 93, 104, 110, 134, 137, 160, 169, 171, 173, 175, 184, 201, 222,
          235, 247, 260, 284, 290, 291, 302, 304, 341, 345)
censor <- c(1, 1, 1, 1, 1, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1, 1, 1, 1, 0, 0,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0)

# R's standard model. The report handles tied event times by Breslow's
# approximation, so ties = "breslow" is given (R's default is Efron's method, which
# gives b = 0.961322 here). The report's z and P are the Wald test of each
# coefficient, its deviance chi-square is R's likelihood ratio test, and its hazard
# ratio and 95% limits are exp(coef) with the lower .95 and upper .95 limits.
fit <- coxph(Surv(time, censor) ~ stage, ties = "breslow")
print(summary(fit))

# The report's lines to 6 places
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
ll <- fit$loglik                     # log likelihood without, then with, the covariates
dev <- 2 * (ll[2] - ll[1])
df <- length(coef(fit))
cat(fit$n, "subjects with", fit$nevent, "events\n")
cat("Deviance (likelihood ratio) chi-square =", six(dev), " df =", df, " ",
    pv(pchisq(dev, df, lower.tail = FALSE)), "\n")
s <- summary(fit)$coefficients
cat("Stage group b1 =", six(s[1, "coef"]), " z =", six(s[1, "z"]), " ",
    pv(s[1, "Pr(>|z|)"]), "\n")

# Hazard ratios: exp of the coefficient, with Wald limits on the log scale
half <- qnorm(0.975) * s[1, "se(coef)"]
cat("Stage group hazard ratio =", six(exp(s[1, "coef"])), " 95% CI =",
    six(exp(s[1, "coef"] - half)), "to", six(exp(s[1, "coef"] + half)), "\n")
cat("Stage group coefficient =", six(s[1, "coef"]), " standard error =",
    six(s[1, "se(coef)"]), "\n")

# Model analysis: the log likelihoods and their deviance chi-square
cat("Log likelihood with no covariates =", six(ll[1]), "\n")
cat("Log likelihood with all model covariates =", six(ll[2]), "\n")
cat("Deviance (likelihood ratio) chi-square =", six(dev), " df =", df, " ",
    pv(pchisq(dev, df, lower.tail = FALSE)), "\n")

# The report's "baseline survival and hazard" option gives, at each event time,
# the survival probability for a subject with every predictor at zero (or at its
# mean when it is continuous and centred) by the product-limit method of
# Kalbfleisch and Prentice, and the cumulative hazard by the Breslow estimator.
# In R these are survfit's stype = 1 and ctype = 1 at that covariate value. R's
# direct estimate agrees with the report's to 6 places here, though the two can
# differ at a time when everyone still at risk dies at the same time.
base <- survfit(fit, newdata = data.frame(stage = 0), stype = 1, ctype = 1)
at_event <- base$n.event > 0
cat("First baseline rows: time, survival probability, cumulative hazard\n")
print(data.frame(time = base$time[at_event], survival = six(base$surv[at_event]),
                 hazard = six(base$cumhaz[at_event]))[1:5, ], row.names = FALSE)
