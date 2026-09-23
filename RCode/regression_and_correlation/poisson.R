# Poisson regression: the StatsDirect help example (Armitage, Berry and Matthews
# 2001, cancers in veterans and non-veterans by age group, with the subject-years
# of exposure) in R
cancers <- c(6, 21, 54, 118, 97, 58, 56, 54, 34, 9, 2,
             18, 60, 122, 191, 108, 74, 88, 120, 141, 108, 99)
years <- c(60840, 157175, 176134, 186514, 135475, 42620, 25001, 13710, 6163, 1575, 273,
           208487, 303832, 325421, 312242, 165597, 54396, 40716, 33801, 26618, 17404,
           14146)
veterans <- rep(c(1, 0), each = 11)
ages <- c("24 <=", "25-29", "30-34", "35-39", "40-44", "45-49", "50-54", "55-59",
          "60-64", "65-69", "70+")
agegroup <- factor(rep(ages, 2), levels = ages)

# R's standard model: a Poisson regression of the counts with the log of the
# exposure as an offset; the age group factor gives the same dummy variables as
# the report, with the youngest group as the baseline
fit <- glm(cancers ~ veterans + agegroup + offset(log(years)), family = poisson)
print(summary(fit))

# The report's lines to 6 places
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
dev <- fit$deviance
lr <- fit$null.deviance - dev
cat("Deviance goodness of fit chi-square =", six(dev), " df =", fit$df.residual, " ",
    pv(pchisq(dev, fit$df.residual, lower.tail = FALSE)), "\n")
df_lr <- fit$df.null - fit$df.residual
cat("Deviance (likelihood ratio) chi-square =", six(lr), " df =", df_lr, " ",
    pv(pchisq(lr, df_lr, lower.tail = FALSE)), "\n")
s <- summary(fit)$coefficients
lab <- c("Intercept", "Veterans", paste0("Age group (", ages[-1], ")"))
for (i in 1:12) {
  cat(lab[i], " b", i - 1, " = ", six(s[i, 1]), "  z = ", six(s[i, 3]), "  ",
      pv(s[i, 4]), "\n", sep = "")
}

signed <- paste0(ifelse(s[3:12, 1] >= 0, "+", ""), six(s[3:12, 1]), " ", lab[3:12])
cat("log Cancers [offset log(Subject-years)] =", six(s[1, 1]), six(s[2, 1]), "Veterans",
    paste(signed, collapse = " "), "\n")

# Incidence rate ratios: exp of the coefficients, with Wald limits (the intercept's
# row is the baseline rate per subject-year)
ci <- confint.default(fit)
for (i in 1:12) {
  cat(lab[i], six(s[i, 1]), six(exp(s[i, 1])), six(exp(ci[i, 1])), "to",
      six(exp(ci[i, 2])), "\n")
}

# The report also gives the ratios in each veteran status: among the veterans
# each age group's rate relative to the youngest non-veterans is exp of the age
# coefficient plus the veteran coefficient, with the standard error of that sum
# from the covariance matrix; among the non-veterans they are the ratios above
vc <- vcov(fit)
for (i in 3:12) {
  est <- s[i, 1] + s[2, 1]
  se_sum <- sqrt(vc[i, i] + vc[2, 2] + 2 * vc[i, 2])
  half <- qnorm(0.975) * se_sum
  cat("Veterans = 1:", lab[i], six(s[i, 1]), six(exp(est)), six(exp(est - half)), "to",
      six(exp(est + half)), "\n")
}

# The model analysis: the log likelihood (as R's logLik gives it), the information
# criteria on the deviance scale, the two pseudo R-squares (1 minus the ratio of
# the deviances; 1 minus the ratio of the log likelihoods, McFadden's, with the
# offset alone as the null model), Pearson's chi-square and the over-dispersion
# scale, which is Pearson's chi-square over its degrees of freedom
ll <- as.numeric(logLik(fit))
ll0 <- as.numeric(logLik(glm(cancers ~ offset(log(years)), family = poisson)))
k <- length(coef(fit))
n <- length(cancers)
cat("Log likelihood with all covariates =", six(ll), "\n")
cat("Deviance with all covariates =", six(dev), " df =", fit$df.residual,
    " rank =", k, "\n")
cat("Akaike information criterion =", six(dev + 2 * k), "\n")
cat("Schwarz information criterion =", six(dev + k * log(n)), "\n")
cat("Deviance with no covariates =", six(fit$null.deviance), "\n")
cat("Pseudo (deviance based) R-square =", six(1 - dev / fit$null.deviance), "\n")
cat("Pseudo (McFadden, likelihood ratio index) R-square =", six(1 - ll / ll0), "\n")
pearson <- sum(residuals(fit, type = "pearson")^2)
cat("Pearson goodness of fit =", six(pearson), " df =", fit$df.residual, " ",
    pv(pchisq(pearson, fit$df.residual, lower.tail = FALSE)), "\n")
scale <- pearson / fit$df.residual
cat("Over-dispersion scale parameter =", six(scale), "\n")
cat("Scaled G2 =", six(lr / scale), " df =", df_lr, " ",
    pv(pchisq(lr / scale, df_lr, lower.tail = FALSE)), "\n")
cat("Scaled Pearson goodness of fit =", six(pearson / scale), " df =", fit$df.residual,
    " ", pv(pchisq(pearson / scale, fit$df.residual, lower.tail = FALSE)), "\n")
cat("Scaled Deviance goodness of fit =", six(dev / scale), " df =", fit$df.residual,
    " ", pv(pchisq(dev / scale, fit$df.residual, lower.tail = FALSE)), "\n")
cat("Coefficients, standard errors, then the scaled standard errors and Wald z:\n")
for (i in 1:12) {
  cat(lab[i], six(s[i, 1]), six(s[i, 2]), six(s[i, 2] * sqrt(scale)),
      six(s[i, 1] / (s[i, 2] * sqrt(scale))),
      pv(2 * pnorm(-abs(s[i, 1] / (s[i, 2] * sqrt(scale))))), "\n")
}
