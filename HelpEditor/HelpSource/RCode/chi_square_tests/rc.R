# R by c contingency table: the StatsDirect help example (Armitage and Berry 1994,
# grief of 66 mothers after a neonatal death by the support they received) in R
counts <- matrix(c(17, 9, 8,
                   6, 5, 1,
                   3, 5, 4,
                   1, 2, 5), 4, 3, byrow = TRUE,
                 dimnames = list(Grief = c("I", "II", "III", "IV"),
                                 Support = c("Good", "Adequate", "Poor")))
print(addmargins(counts))
n <- sum(counts)
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}

# Nominal independence: Pearson's chi-square without Yates' correction (R warns
# that the expected counts are small, as the report does), the expected counts
# and each cell's share of the chi-square, the likelihood ratio G-square and the
# Fisher-Freeman-Halton exact test, which fisher.test gives for a table larger
# than 2 by 2
chi <- suppressWarnings(chisq.test(counts, correct = FALSE))
cat("Expected\n")
print(six(chi$expected), quote = FALSE)
cat("DChi2\n")
print(six(chi$residuals^2), quote = FALSE)
cat("Warning:", sum(chi$expected < 5), "out of", length(counts),
    "cells have EXPECTATION < 5\n")
cat("Chi-square =", six(chi$statistic), " DF =", chi$parameter, " ", pv(chi$p.value),
    "\n")
g2 <- 2 * sum(counts * log(counts / chi$expected), na.rm = TRUE)  # 0 log 0 counts as 0
cat("G-square =", six(g2), " DF =", chi$parameter, " ",
    pv(pchisq(g2, chi$parameter, lower.tail = FALSE)), "\n")
cat("Fisher-Freeman-Halton exact", pv(fisher.test(counts)$p.value), "\n")

# The trend and ANOVA statistics score the rows 1 to 4 and the columns 1 to 3
# (equally spaced scores, as the report uses unless others are given). One pair
# of scores per mother makes them ordinary vectors of 66 values (whole-number
# counts, as a contingency table has).
grief <- rep(as.vector(row(counts)), as.vector(counts))
support <- rep(as.vector(col(counts)), as.vector(counts))

# ANOVA: does the mean grief score differ between the support columns? The
# chi-square is (n - 1) times the between-column sum of squares over the total
# sum of squares of the row scores, with columns less 1 as its degrees of freedom
# (the report counts only columns and rows with any counts)
ss <- anova(lm(grief ~ factor(support)))[["Sum Sq"]]
chi_eq <- (n - 1) * ss[1] / sum(ss)
df_eq <- ncol(counts) - 1
cat("Chi-square for equality of mean column scores =", six(chi_eq), " DF =", df_eq, " ",
    pv(pchisq(chi_eq, df_eq, lower.tail = FALSE)), "\n")

# Linear trend: the correlation between the row and column scores, and the
# Mantel-Haenszel chi-square (n - 1) r^2 with one degree of freedom
r <- cor(grief, support)
m2 <- (n - 1) * r^2
cat("Sample correlation (r) =", six(r), "\n")
cat("Chi-square for linear trend (M2) =", six(m2), " DF = 1 ",
    pv(pchisq(m2, 1, lower.tail = FALSE)), "\n")

# Nominal association: phi, Pearson's contingency coefficient and Cramer's V
x2 <- as.numeric(chi$statistic)
cat("Phi =", six(sqrt(x2 / n)), "\n")
cat("Pearson's contingency =", six(sqrt(x2 / (x2 + n))), "\n")
cat("Cramer's V =", six(sqrt(x2 / (n * (min(dim(counts)) - 1)))), "\n")

# Ordinal association: Goodman and Kruskal's gamma and Kendall's tau-b from the
# concordant and discordant pairs, each with two standard errors (Agresti 2002,
# Brown and Benedetti 1977): one for a test that the measure is 0, one under
# independence
R <- nrow(counts)
C <- ncol(counts)
conc <- disc <- counts * 0
for (i in 1:R) for (j in 1:C) {
  conc[i, j] <- sum(counts[(1:R) > i, (1:C) > j]) + sum(counts[(1:R) < i, (1:C) < j])
  disc[i, j] <- sum(counts[(1:R) > i, (1:C) < j]) + sum(counts[(1:R) < i, (1:C) > j])
}
cc <- sum(counts * conc)                  # concordant pairs, counted twice
dc <- sum(counts * disc)                  # discordant pairs, counted twice
gamma <- (cc - dc) / (cc + dc)
se_gamma <- 4 / (cc + dc)^2 * sqrt(sum(counts * (dc * conc - cc * disc)^2))
v_ind <- sum(counts * (conc - disc)^2) - (cc - dc)^2 / n
se_gamma_ind <- 2 / (cc + dc) * sqrt(v_ind)
rows <- rowSums(counts)
cols <- colSums(counts)
dr <- n^2 - sum(rows^2)
dcl <- n^2 - sum(cols^2)
taub <- (cc - dc) / sqrt(dr * dcl)
vij <- outer(rows, cols, function(a, b) a * dcl + b * dr)
vt <- sum(counts * (2 * sqrt(dr * dcl) * (conc - disc) + taub * vij)^2) -
      n^3 * taub^2 * (dr + dcl)^2
se_taub <- sqrt(vt) / (dr * dcl)
se_taub_ind <- 2 * sqrt(v_ind / (dr * dcl))
z <- qnorm(0.975)
show <- function(label, est, se) {
  cat(label, " SE =", six(se), " ", pv(2 * pnorm(-abs(est / se))),
      " 95% CI =", six(est - z * se), "to", six(est + z * se), "\n")
}
cat("Goodman-Kruskal gamma =", six(gamma), "\n")
show("Approximate test of gamma = 0:", gamma, se_gamma)
show("Approximate test of independence:", gamma, se_gamma_ind)
cat("Kendall tau-b =", six(taub), "\n")
show("Approximate test of tau-b = 0:", taub, se_taub)
show("Approximate test of independence:", taub, se_taub_ind)
