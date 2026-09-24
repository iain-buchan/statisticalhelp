# Error bar plot: the StatsDirect help illustration (mean urate, mmol/l, with one
# standard deviation either side, at ten times since conception; the test
# workbook's Graphics worksheet columns Weeks since conception, Mean Urate (mmol/l),
# SD Urate (mmol/l), Mean-SD and Mean+SD) in R
weeks <- c(0, 4, 8, 12, 16, 24, 32, 36, 38, 50)
urate <- c(0.246, 0.240, 0.190, 0.173, 0.189, 0.196, 0.216, 0.232, 0.269, 0.275)
sd_urate <- c(0.059, 0.044, 0.070, 0.047, 0.048, 0.040, 0.053, 0.056, 0.056, 0.057)

# The ends of the bars, one standard deviation below and above each mean: the
# workbook's Mean-SD and Mean+SD columns hold these, made from the mean and sd
# columns with apply function. Any error function can be used in their place, for
# example the standard error or the confidence limits of each mean.
lower <- urate - sd_urate
upper <- urate + sd_urate

# StatsDirect draws a hollow marker at each mean and a vertical bar from the lower
# to the upper value with a short line across each end, on axes that take in the
# whole of every bar and carry the column titles. Base R has no error bar function:
# arrows() with a 90 degree head at both ends draws the same bars (a bar of zero
# length would be skipped with a warning).
plot(weeks, urate, pch = 1, ylim = range(lower, urate, upper), bty = "l",
     xlab = "Weeks since conception", ylab = "Mean Urate (mmol/l)",
     main = "Error bar plot")
arrows(weeks, lower, weeks, upper, angle = 90, code = 3, length = 0.05)
# The chart options can join the markers with lines, as lines(weeks, urate) would.

# The values plotted
cat("Weeks since conception   Mean Urate (mmol/l)   Bar (mean - sd to mean + sd)\n")
for (i in seq_along(weeks)) {
  cat(sprintf("%-24d %-21.3f %.3f to %.3f\n", weeks[i], urate[i], lower[i], upper[i]))
}
