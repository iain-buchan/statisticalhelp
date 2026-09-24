# Wei-Lachin test: the StatsDirect help example (Makuch and Escobar 1991, days for
# lymphocyte cultures from 47 HIV patients in two treatment groups to express p24
# antigen, over four periods) in R. Save the test workbook's Survival worksheet columns
# Treatment Gp, time m1, censor m1, ... time m4, censor m4, with their headings, as
# p24_antigen.csv in R's working directory first
p24 <- read.csv("p24_antigen.csv")
group <- p24$Treatment.Gp
times <- as.matrix(p24[, paste0("time.m", 1:4)])
censor <- as.matrix(p24[, paste0("censor.m", 1:4)])   # 1 = event, 0 = censored

# One repeat time on its own is an ordinary two-group survival comparison, which the
# survival package (it comes with R) tests with survdiff(): rho = 0 is the log-rank
# test and rho = 1 the Peto-Peto version of the Wilcoxon test. Their chi-squares differ
# from the Wei-Lachin univariate ones below, which weight by the proportion at risk
# (Gehan) and use Wei and Lachin's variance estimate rather than the hypergeometric one
library(survival)
print(survdiff(Surv(times[, 1], censor[, 1]) ~ group))

# Neither base R nor the survival package has the Wei-Lachin test, so it is computed by
# the formulae above, following Makuch and Escobar (1991). For each repeat time and
# each event, e is the expected share of the event for each group (its number at risk
# over the total at risk), weighted by w: 1 for the log-rank method, or the proportion
# of subjects at risk for Gehan's generalised Wilcoxon method. Each subject then has a
# score for each repeat time (its weighted expected share for the other group if it had
# an event, less the running sum psi of the expected shares of the events in its own
# group at or before its own time, each divided by the number of its group then at
# risk), and the covariance matrix of the statistics is the mean cross product of these
# scores
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
wei_lachin <- function(times, censor, group, gehan) {
  g <- match(group, unique(group))   # the groups numbered in order of first appearance
  n <- nrow(times)
  m <- ncol(times)
  t_k <- numeric(m)
  failures <- matrix(0, 2, m)
  scores <- matrix(0, n, m)
  for (k in 1:m) {
    x <- times[, k]
    s <- censor[, k]
    y1 <- sapply(x, function(t) sum(g == 1 & x >= t))   # at risk in each group at
    y2 <- sapply(x, function(t) sum(g == 2 & x >= t))   # each subject's own time
    w <- if (gehan) (y1 + y2) / n else 1
    e1 <- ifelse(s == 1, w * y1 / (y1 + y2), 0)
    e2 <- ifelse(s == 1, w * y2 / (y1 + y2), 0)
    mu <- ifelse(g == 1, e2, e1)      # the expected share for the other group
    t_k[k] <- (sum(mu[g == 1]) - sum(mu[g == 2])) / sqrt(n)
    failures[, k] <- tabulate(g[s == 1], 2)
    y_own <- ifelse(g == 1, y1, y2)
    psi <- sapply(1:n, function(j) {
      earlier <- g == g[j] & s == 1 & x <= x[j]
      sum(mu[earlier] / y_own[earlier])
    })
    scores[, k] <- mu - psi
  }
  sigma <- crossprod(scores) / n
  list(n = n, by_group = tabulate(g, 2), failures = failures, t = t_k, sigma = sigma)
}

# The report gives the Gehan (generalised Wilcoxon) results first, then the log-rank
# ones. The univariate chi-square is (t / standard error)^2; the omnibus statistic is
# t' inverse(sigma) t on m degrees of freedom, and the stochastic ordering z is the sum
# of the t over the square root of the sum of the whole covariance matrix. solve()
# needs the covariance matrix to be of full rank, as it is here (StatsDirect uses a
# generalised inverse, which is the same when the matrix is of full rank)
lower <- function(a) {
  for (i in seq_len(nrow(a))) cat(six(a[i, 1:i]), "\n")
}
for (gehan in c(TRUE, FALSE)) {
  method <- if (gehan) "Generalised Wilcoxon (Gehan)" else "Log-Rank"
  wl <- wei_lachin(times, censor, group, gehan)
  m <- length(wl$t)
  cat("\nUnivariate", method, "\n")
  cat("total cases = ", wl$n, " (by group = ", wl$by_group[1], " and ", wl$by_group[2],
      ")\n", sep = "")
  for (k in 1:m) {
    chi <- wl$t[k]^2 / wl$sigma[k, k]
    cat("Repeat time", k, "\n")
    cat("observed failures by group =", wl$failures[1, k], "and", wl$failures[2, k],
        "\n")
    cat("Wei-Lachin t =", six(wl$t[k]), "\n")
    cat("Wei-Lachin variance =", six(wl$sigma[k, k]), "\n")
    cat("chi-square =", six(chi), " ", pv(pchisq(chi, 1, lower.tail = FALSE)), "\n")
  }
  cat("\nMultivariate", method, "\n")
  cat("Covariance matrix:\n")
  lower(wl$sigma)
  cat("Inverse of covariance matrix:\n")
  lower(solve(wl$sigma))
  omnibus <- drop(t(wl$t) %*% solve(wl$sigma) %*% wl$t)
  cat("repeat times =", m, "\n")
  cat("chi-square omnibus statistic =", six(omnibus), " ",
      pv(pchisq(omnibus, m, lower.tail = FALSE)), "\n")
  z <- sum(wl$t) / sqrt(sum(wl$sigma))
  p1 <- pnorm(-abs(z))
  cat("stochastic ordering z = ", six(z), "  one sided ", pv(p1), ",  two sided ",
      pv(2 * p1), "\n", sep = "")
}
