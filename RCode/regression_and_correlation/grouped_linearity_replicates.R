# Linearity with replicates of Y: the StatsDirect help example (Armitage and Berry
# 1994, bone density of rats at three log doses of vitamin D; the test workbook's
# columns BD 1_Std, BD 2_Std and BD 3_Std hold the replicates at each dose) in R
dose <- c(0.544, 0.845, 1.146)
bd <- list(c(0, 0, 1, 2.75, 2.75, 1.75, 2.75, 2.25, 2.25, 2.5),
           c(1.5, 2.5, 5, 6, 4.25, 2.75, 1.5, 3),
           c(2, 2.5, 5, 4, 5, 4, 2.5, 3.5, 3, 2, 3, 4, 4))
x <- rep(dose, lengths(bd))
y <- unlist(bd)

# The test of linearity compares the straight line with the model of a separate
# mean for each dose: R's anova of the two fits gives the deviation from linearity
# (its "Residuals" line is the within dose residual) and the regression line is
# the straight line's own analysis of variance
linear <- lm(y ~ x)
means <- lm(y ~ factor(x))
print(anova(linear, means))
print(anova(linear))

# The report's table to 6 places
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
ssreg <- anova(linear)$"Sum Sq"[1]
ssdev <- anova(linear, means)$"Sum of Sq"[2]
sswithin <- anova(means)$"Sum Sq"[2]
dfwithin <- anova(means)$Df[2]
mswithin <- sswithin / dfwithin
cat("Due to regression", six(ssreg), 1, six(ssreg), six(ssreg / mswithin),
    pv(pf(ssreg / mswithin, 1, dfwithin, lower.tail = FALSE)), "\n")
cat("Deviation of x means", six(ssdev), 1, six(ssdev), six(ssdev / mswithin),
    pv(pf(ssdev / mswithin, 1, dfwithin, lower.tail = FALSE)), "\n")
cat("Within x residual", six(sswithin), dfwithin, six(mswithin), "\n")
cat("Total", six(ssreg + ssdev + sswithin), length(y) - 1, "\n")
