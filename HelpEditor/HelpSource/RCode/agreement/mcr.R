# Method comparison regression: the StatsDirect help example (the creatinine data of
# the mcr package, serum and plasma creatinine in 108 subjects; the test workbook's
# Agreement worksheet columns Serum Creatinine and Plasma Creatinine hold the same
# pairs) in R. StatsDirect runs this analysis through the mcr package, so install it
# first with install.packages("mcr")
library(mcr)
data(creatinine)
complete <- complete.cases(creatinine)
serum <- creatinine$serum.crea[complete]
plasma <- creatinine$plasma.crea[complete]

# Deming regression of the test method (plasma) on the reference method (serum) with
# an error ratio of 1, its standard errors and confidence intervals from Linnet's
# jackknife: the package's summary, then its coefficient table to 6 places
fit <- mcreg(serum, plasma, error.ratio = 1, method.reg = "Deming",
             method.ci = "jackknife", mref.name = "Serum Creatinine",
             mtest.name = "Plasma Creatinine")
invisible(printSummary(fit))
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
est <- getCoefficients(fit)
cat("Sample size:", length(serum), "\n")
for (term in c("Slope", "Intercept")) {
  cat(paste0(term, ":"), six(est[term, "EST"]), six(est[term, "SE"]),
      six(est[term, "LCI"]), "to", six(est[term, "UCI"]), "\n")
}

# The same arithmetic without the package. With an error ratio of 1 the Deming line
# is the orthogonal regression, from the sums of squares and products about the
# means. The jackknife refits the line leaving each pair out in turn; the standard
# error is that of the pseudo-values n b - (n - 1) b(-i) over root n, and the limits
# use t on n - 2 degrees of freedom (Linnet 1993)
deming <- function(x, y) {
  sxx <- sum((x - mean(x))^2)
  syy <- sum((y - mean(y))^2)
  sxy <- sum((x - mean(x)) * (y - mean(y)))
  b <- (syy - sxx + sqrt((syy - sxx)^2 + 4 * sxy^2)) / (2 * sxy)
  c(Intercept = mean(y) - b * mean(x), Slope = b)
}
n <- length(serum)
whole <- deming(serum, plasma)
left_out <- t(sapply(1:n, function(i) deming(serum[-i], plasma[-i])))
pseudo <- n * rep(whole, each = n) - (n - 1) * left_out
se <- apply(pseudo, 2, sd) / sqrt(n)
for (term in c("Slope", "Intercept")) {
  cat(paste0(term, ":"), six(whole[term]), six(se[term]),
      six(whole[term] - qt(0.975, n - 2) * se[term]), "to",
      six(whole[term] + qt(0.975, n - 2) * se[term]), "\n")
}

# The report's plot: the test method against the reference method with the Deming
# line, its confidence band and the line of identity
plot(fit, x.lab = "Serum Creatinine", y.lab = "Plasma Creatinine")
