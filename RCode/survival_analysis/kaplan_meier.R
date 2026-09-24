# Kaplan-Meier survival estimates: the StatsDirect help example (a hypothetical study
# of death from cancer in two groups of rats given different pre-treatments; the test
# workbook's Survival worksheet columns Group Surv, Time Surv and Censor Surv) in R
library(survival)
group <- c(2, 1, 2, 2, 1, 1, 1, 1, 1, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2,
           2, 2, 1, 2, 2, 1, 1, 2, 1, 2, 2, 2, 2, 1, 2, 2)
time <- c(142, 143, 157, 163, 165, 188, 188, 190, 192, 198, 204, 205, 206, 208, 212,
          216, 216, 220, 227, 230, 232, 232, 232, 233, 233, 233, 233, 235, 239, 240,
          244, 246, 261, 265, 280, 280, 295, 295, 303, 323, 344)
event <- c(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1,
           1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0)   # 1 = death, 0 = censored

# The groups in the order in which they first occur, as StatsDirect lists them
g <- factor(group, levels = unique(group))

# R's standard estimate: survfit gives the product limit estimate of S with Greenwood's
# variance. conf.type = "plain" makes its limits S +/- z SE(S), the scale on which the
# Brookmeyer-Crowley interval for the median is set (R's default is the log scale).
# print(fit) gives the median (the first time at which S falls to 0.5 or below; R
# takes the midpoint when S equals 0.5 exactly) and, with rmean = "individual", the
# mean survival time restricted to each group's own longest time, as in the report.
# R's limits for the median are where the confidence band of S crosses 0.5, so its
# upper limit is the first time at which the upper limit of S is 0.5 or below, where
# StatsDirect's is the last time whose S is within z standard errors of 0.5.
fit <- survfit(Surv(time, event) ~ g, conf.type = "plain")
print(fit, rmean = "individual")
print(summary(fit, censored = TRUE))

# The report's tables: every distinct time, with the cumulative hazard H = -ln(S)
# (Peterson) and its standard error from Greenwood's sum, which is SE(S) / S
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
z <- qnorm(0.975)
s <- summary(fit, censored = TRUE)
H <- log(1 / s$surv)
means <- summary(fit, rmean = "individual")$table
for (k in levels(s$strata)) {
  i <- s$strata == k
  t <- s$time[i]
  n <- s$n.risk[i]
  d <- s$n.event[i]
  S <- s$surv[i]
  seS <- s$std.err[i]
  seH <- sqrt(cumsum(d / (n * (n - d))))   # infinite once S reaches 0
  cat("\nGroup Surv =", sub("^g=", "", k), "\n")
  print(data.frame(Time = t, "At risk" = n, Dead = d, Censored = s$n.censor[i],
                   S = six(S), "SE(S)" = ifelse(S > 0, six(seS), "*"),
                   H = ifelse(S > 0, six(H[i]), "infinity"),
                   "SE(H)" = ifelse(S > 0, six(seH), "*"), check.names = FALSE),
        row.names = FALSE)

  # The median: the first time at which S is 0.5 or below (this example has no run of
  # S exactly 0.5, so R's midpoint rule gives the same value)
  m <- which(S <= 0.5)[1]
  # (a group whose S never falls to 0.5 has no median; the two intervals below need one)
  cat("Median survival time =", t[m], "\n")

  # Andersen's interval: the median +/- z SE(S at the median) / f, where f estimates the
  # density of the survival times at the median by the slope of S between the last time
  # with S >= 0.5 + 0.05 and the first time with S <= 0.5 - 0.05 (0.05 = 1 - 0.95)
  hi <- max(which(S >= 0.55))
  lo <- min(which(S <= 0.45))
  f <- (S[hi] - S[lo]) / (t[lo] - t[hi])
  cat("Andersen 95% CI for median survival time =", six(t[m] - z * seS[m] / f), "to",
      six(t[m] + z * seS[m] / f), "\n")

  # Brookmeyer-Crowley: the times whose S is within z standard errors of 0.5
  bc <- range(t[S > 0 & abs(S - 0.5) / seS <= z])
  cat("Brookmeyer-Crowley 95% CI for median survival time =", bc[1], "to", bc[2], "\n")

  # The mean survival time (the area under S up to the longest time), with the
  # standard error R gives it multiplied by sqrt(m / (m - 1)) for m deaths, the
  # correction of Hosmer and Lemeshow (1999) that StatsDirect applies
  deaths <- means[k, "events"]   # the factor needs at least two deaths
  mu <- means[k, "rmean"]
  se <- means[k, "se(rmean)"] * sqrt(deaths / (deaths - 1))
  last_death <- max(t[d > 0])
  lim <- ""
  if (max(t) > last_death) lim <- sprintf("[limit: %s on %s] ", max(t), last_death)
  cat(sprintf("Mean survival time (95%% CI) %s= %s (%s to %s)\n", lim, six(mu),
              six(mu - z * se), six(mu + z * se)))
}

# The survival plot that follows in the report, with a tick at each censored time
plot(fit, mark.time = TRUE, pch = "|", col = 1:2, xlab = "Times", ylab = "Survivor",
     main = "Survival Plot (PL estimates)")
legend("topright", levels(g), col = 1:2, lty = 1, title = "Group Surv", bty = "n")

# The hazard plots: each group's H against time as a step function, then the plots
# whose straight line would indicate a Weibull (log H against log time), log-normal
# (z(S) against log time) or linear hazard rate (H / t against time) distribution.
# Rows where H is infinite, or where the transform is undefined, are left out. The
# hazard plot starts at H = 0 on the left of the axis, as StatsDirect draws it.
km_plot <- function(x, y, keep, xlab, ylab, main, where = "topleft", from = NULL) {
  x <- split(x[keep], s$strata[keep])
  y <- split(y[keep], s$strata[keep])
  plot(range(unlist(x)), range(unlist(y)), type = "n", xlab = xlab, ylab = ylab,
       main = main)
  for (k in seq_along(x)) {
    if (is.null(from)) lines(x[[k]], y[[k]], type = "s", col = k)
    else lines(c(min(unlist(x)), x[[k]]), c(from, y[[k]]), type = "s", col = k)
  }
  legend(where, levels(g), col = seq_along(x), lty = 1, title = "Group Surv", bty = "n")
}
km_plot(s$time, H, is.finite(H), "Times", "Hazard", "Hazard Plot", from = 0)
km_plot(log(s$time), log(H), is.finite(H) & H > 0, "Log Times", "Log Hazard",
        "Log Hazard Plot")
km_plot(log(s$time), qnorm(s$surv), is.finite(qnorm(s$surv)), "Log Times",
        "Z (Survivor)", "Lognormal Survival Plot", "topright")
km_plot(s$time, H / s$time, is.finite(H), "Times", "Hazard / Time", "Hazard Rate Plot")
