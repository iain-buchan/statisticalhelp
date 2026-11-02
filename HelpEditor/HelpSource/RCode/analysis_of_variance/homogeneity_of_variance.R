# Squared ranks test for equality of variance: the StatsDirect help example
# (Conover 1999, weights of cereal boxes filled by two machines) in R
x <- c(10.8, 11.1, 10.4, 10.1, 11.3)                    # Machine X
y <- c(10.8, 10.5, 11.0, 10.9, 10.8, 10.7, 10.8)        # Machine Y

# R has no squared ranks test, so it is calculated as Conover (1999) describes:
# each observation's absolute deviation from its sample mean is ranked over
# both samples together (mid-ranks for ties), and the test statistic is the
# sum of the squared ranks of the first sample, standardised with the
# correction for ties
u <- c(abs(x - mean(x)), abs(y - mean(y)))
r <- rank(u)
n1 <- length(x)
n2 <- length(y)
N <- n1 + n2
ssq <- sum(r[1:n1]^2)
r2bar <- mean(r^2)
z <- (ssq - n1 * r2bar) /
  sqrt(n1 * n2 / (N * (N - 1)) * sum(r^4) - n1 * n2 / (N - 1) * r2bar^2)
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
cat("z =", six(z), "\n")
cat("Two tailed", pv(2 * pnorm(-abs(z))), "\n")
cat("One tailed", pv(pnorm(-abs(z))), "\n")

# The parametric tests, illustrated with the one way ANOVA example (Armitage
# and Berry 1994, worms recovered from four groups of rats): StatsDirect gives
# them as an option of One Way, in one report with the Welch adjusted ANOVA
worms <- c(279, 338, 334, 198, 303, 378, 275, 412, 265, 286,
           172, 335, 335, 282, 250, 381, 346, 340, 471, 318)
group <- factor(rep(paste("Expt", 1:4), each = 5))

# Levene's test in its W50 form (Brown and Forsythe 1974) is a one way ANOVA
# of the absolute deviations from the group medians
dev <- abs(worms - ave(worms, group, FUN = median))
lev <- anova(aov(dev ~ group))
dfs <- function(a, b) paste0("(df = ", a, ", ", b, ")")
cat("Levene's (W50) F =", six(lev$"F value"[1]), "", dfs(lev$Df[1], lev$Df[2]), "",
    pv(lev$"Pr(>F)"[1]), "\n")

# Bartlett's test and the Welch ANOVA are standard R functions
b <- bartlett.test(worms, group)
cat("Bartlett's chi-square =", six(b$statistic), " df =", b$parameter, "",
    pv(b$p.value), "\n")
w <- oneway.test(worms ~ group)                         # var.equal = FALSE is Welch
cat("Welch adjusted ANOVA F =", six(w$statistic), "",
    dfs(w$parameter[1], six(w$parameter[2])), "", pv(w$p.value), "\n")
