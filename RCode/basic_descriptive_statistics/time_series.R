# Time series summary: the StatsDirect help example (Bland 2000, blood levels of
# zidovudine after an oral dose in patients with and without malabsorption) in R
# The data are the Group, Time, Zidovudine and Patient columns of the Other
# worksheet of the StatsDirect test workbook. Save those columns, with their
# headings, as zidovudine.csv in R's working directory first.
d <- read.csv("zidovudine.csv")
# 2 decimal places, with halves rounded up as StatsDirect rounds them
two <- function(x) {
  formatC(sign(x) * floor(abs(x) * 100 + 0.5 + 1e-9) / 100, digits = 2, format = "f",
          drop0trailing = TRUE)
}
iqr <- function(x) diff(quantile(x, c(0.25, 0.75), type = 2))

# R has no standard function for this summary, so it is built here. For each
# group and time: the number of observations, mean, SD, SE, median and IQR
# (quartiles by centile type 1, which is type 2 in R).
for (g in unique(d$Group)) {
  cat("Group:", g, "\n")
  s <- d[d$Group == g, ]
  cat("Time  Observations  Mean  SD  SE  Median  IQR\n")
  for (t in sort(unique(s$Time))) {
    y <- s$Zidovudine[s$Time == t]
    cat(t, length(y), two(mean(y)), two(sd(y)), two(sd(y) / sqrt(length(y))),
        two(median(y)), two(iqr(y)), "\n")
  }

  # For each subject: the baseline (the value at time 0), the smallest and
  # largest values, the time of the largest, the slope of a least squares line
  # through the values up to the largest, and the area under the curve by the
  # trapezium rule
  cat("Subject ID  Baseline  Min. observation  Max. observation  Time to max.",
      "Slope to max.  AUC\n")
  auc <- slope <- tmax <- c()
  for (id in sort(unique(s$Patient))) {
    p <- s[s$Patient == id, ]
    p <- p[order(p$Time), ]
    y <- p$Zidovudine
    t <- p$Time
    k <- which.max(y)
    tmax[id] <- t[k]
    slope[id] <- coef(lm(y[1:k] ~ t[1:k]))[2]
    auc[id] <- sum(diff(t) * (y[-1] + y[-length(y)]) / 2)
    cat(id, two(y[1]), two(min(y)), two(max(y)), t[k], two(slope[id]), two(auc[id]),
        "\n")
  }

  # The group's summary of the areas: t and z intervals for the mean, and a
  # bootstrap-t interval. StatsDirect's bootstrap uses its own random numbers, so
  # its limits differ a little from these.
  auc <- auc[!is.na(auc)]
  slope <- slope[!is.na(slope)]
  tmax <- tmax[!is.na(tmax)]
  n <- length(auc)
  se <- sd(auc) / sqrt(n)
  cat("Subjects:", n, "\n")
  cat("Total (mean per time point) observations:", nrow(s),
      paste0("(", nrow(s) / length(unique(s$Time)), ")"), "\n")
  cat("Mean (SD) area under curve (AUC):", two(mean(auc)),
      paste0("(", two(sd(auc)), ")"), "\n")
  cat("SE of mean AUC:", two(se), "\n")
  cat("95% t-interval for mean AUC:", two(mean(auc) - qt(0.975, n - 1) * se), "to",
      two(mean(auc) + qt(0.975, n - 1) * se), "\n")
  set.seed(2944296)
  tstar <- replicate(10000, {
    b <- sample(auc, n, replace = TRUE)
    (mean(b) - mean(auc)) / (sd(b) / sqrt(n))
  })
  cat("95% bootstrap-t interval for mean AUC (10000 iterations):",
      two(mean(auc) + quantile(tstar, 0.025, type = 2) * se), "to",
      two(mean(auc) + quantile(tstar, 0.975, type = 2) * se), "\n")
  cat("95% z-interval for mean AUC:", two(mean(auc) - qnorm(0.975) * se), "to",
      two(mean(auc) + qnorm(0.975) * se), "\n")
  cat("Median (IQR) area under curve:", two(median(auc)),
      paste0("(", two(iqr(auc)), ")"), "\n")
  cat("Median (IQR) time to maximum:", median(tmax), paste0("(", iqr(tmax), ")"), "\n")
  cat("Median (IQR) slope to maximum:", two(median(slope)),
      paste0("(", two(iqr(slope)), ")"), "\n")
  cat("Mean (SD) slope to maximum:", two(mean(slope)), paste0("(", two(sd(slope)), ")"),
      "\n")

  # The group's chart: each subject's values against time
  plot(s$Time, s$Zidovudine, type = "n", main = g, xlab = "Time", ylab = "Zidovudine")
  for (id in unique(s$Patient)) {
    p <- s[s$Patient == id, ]
    lines(p$Time[order(p$Time)], p$Zidovudine[order(p$Time)], type = "b")
  }
  assign(paste0("auc_", g), auc)
}

# The areas of the two groups compared with Welch's t test (R's default), which
# is what StatsDirect reports, followed in the report by its bootstrap version
r <- t.test(auc_Malabsorbtion, auc_Normal)
cat("Group comparison: Malabsorbtion vs. Normal\n")
cat("Welch t (SE of difference):", two(r$statistic), paste0("(", two(r$stderr), ")"),
    "\n")
cat("DF (Satterthwaite):", two(r$parameter), "\n")
cat("Two sided P =", formatC(r$p.value, digits = 4, format = "f"), "\n")
cat("AUC difference (95% CI):", two(diff(rev(r$estimate))),
    paste0("(", two(r$conf.int[1]), " to ", two(r$conf.int[2]), ")"), "\n")

# The normal plot of the areas that ends the report (all subjects)
qqnorm(c(auc_Malabsorbtion, auc_Normal), main = "Normal Plot for AUC",
       ylab = "Area Under Curve")
qqline(c(auc_Malabsorbtion, auc_Normal))
