# Logistic regression: the StatsDirect help example (Altman 1991, hypertension and
# smoking, obesity and snoring in 433 men) in R
men <- c(60, 17, 8, 2, 187, 85, 51, 23)
hypertensive <- c(5, 2, 1, 0, 35, 13, 15, 8)
smoking <- c(0, 1, 0, 1, 0, 1, 0, 1)
obesity <- c(0, 0, 1, 1, 0, 0, 1, 1)
snoring <- c(0, 0, 0, 0, 1, 1, 1, 1)

# R's standard model for grouped binomial data: responders and non-responders in
# each row. Its residual deviance is the deviance goodness of fit, and the drop
# from the null deviance is the likelihood ratio chi-square for the covariates
fit <- glm(cbind(hypertensive, men - hypertensive) ~ smoking + obesity + snoring,
           family = binomial)
print(summary(fit))
print(exp(cbind(OR = coef(fit), confint.default(fit))))

# The report's lines to 6 places
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
dev <- fit$deviance
lr <- fit$null.deviance - fit$deviance
cat("Deviance goodness of fit chi-square =", six(dev), " df =", fit$df.residual, " ",
    pv(pchisq(dev, fit$df.residual, lower.tail = FALSE)), "\n")
df_lr <- fit$df.null - fit$df.residual
cat("Deviance (likelihood ratio) chi-square =", six(lr), " df =", df_lr, " ",
    pv(pchisq(lr, df_lr, lower.tail = FALSE)), "\n")
s <- summary(fit)$coefficients
ci <- confint.default(fit)   # Wald limits, as the report's
for (i in 2:4) {
  cat(rownames(s)[i], "odds ratio =", six(exp(s[i, 1])), " (", six(exp(ci[i, 1])), "to",
      six(exp(ci[i, 2])), ")  z =", six(s[i, 3]), " ", pv(s[i, 4]), "\n")
}
cat("logit Hypertensive =", six(s[1, 1]), six(s[2, 1]), "Smoking",
    paste0("+", six(s[3, 1])), "Obesity", paste0("+", six(s[4, 1])), "Snoring\n")

# The model analysis. The report's log likelihood is the sum over the men of
# log p or log(1 - p) (without the binomial coefficients that R's logLik adds), the
# information criteria are on the deviance scale (deviance + 2k, deviance + k log n
# for the n rows of data), and the two pseudo R-squares are 1 minus the ratio of
# the deviances and 1 minus the ratio of the log likelihoods (McFadden's)
k <- length(coef(fit))
n <- length(men)
p <- fitted(fit)
ll <- sum(hypertensive * log(p) + (men - hypertensive) * log(1 - p))
p0 <- sum(hypertensive) / sum(men)
ll0 <- sum(hypertensive * log(p0) + (men - hypertensive) * log(1 - p0))
cat("Log likelihood with all covariates =", formatC(ll, digits = 4, format = "f"), "\n")
cat("Deviance with all covariates =", six(dev), " df =", fit$df.residual,
    " rank =", k, "\n")
cat("Akaike =", six(dev + 2 * k), "\n")
cat("Schwarz =", six(dev + k * log(n)), "\n")
cat("Deviance with no covariates =", six(fit$null.deviance), "\n")
cat("Pseudo (deviance based) R-square =", six(1 - dev / fit$null.deviance), "\n")
cat("Pseudo (McFadden, likelihood ratio index) R-square =", six(1 - ll / ll0), "\n")
pearson <- sum(residuals(fit, type = "pearson")^2)
cat("Pearson chi-square goodness of fit =", six(pearson), " df =", fit$df.residual, " ",
    pv(pchisq(pearson, fit$df.residual, lower.tail = FALSE)), "\n")
# The Hosmer-Lemeshow test as the report forms it for grouped data: the covariate
# patterns in order of fitted proportion, a new group whenever the men so far
# reach the next tenth of the total (a pattern is never split), then the sum over
# the groups of (O - E)^2 / (E (1 - E / n)) with 2 fewer degrees of freedom than
# groups; these 8 patterns make 4 groups
ntot <- sum(men)
ncut <- floor(ntot / 10)
ncum <- 0
group <- integer(n)
g <- 1
for (i in order(p)) {
  if (ncum >= ncut && g <= 10) {
    g <- g + 1
    ncut <- floor(max(ncum + ntot / 10, g * ntot / 10))
  }
  group[i] <- g
  ncum <- ncum + men[i]
}
o <- tapply(hypertensive, group, sum)
e <- tapply(p * men, group, sum)
size <- tapply(men, group, sum)
hl <- sum((o - e)^2 / (e * (1 - e / size)))
cat("Hosmer-Lemeshow test =", six(hl), " df =", length(o) - 2, " ",
    pv(pchisq(hl, length(o) - 2, lower.tail = FALSE)), "\n")
cat("Coefficients and standard errors:\n")
for (i in 1:4) cat(rownames(s)[i], six(s[i, 1]), six(s[i, 2]), "\n")
