# Forest (Cochrane) plot: the StatsDirect help illustration (the odds ratio of death
# after heart attack in each of the seven trials of aspirin given by Fleiss (1993),
# with its 95% confidence limits and the size of the trial, and a pooled odds ratio
# for all seven; the test workbook's Graphics worksheet columns Odds Ratio, LCI, UCI,
# Number, Pool and Study) in R
study <- c("MRC-1", "CDP", "MRC-2", "GASP", "PARIS", "AMIS", "ISIS-2", "Pooled")
or <- c(0.719714, 0.68076, 0.80287, 0.800739, 0.798143, 1.132736, 0.894969, 0.896824)
lower <- c(0.47831, 0.446423, 0.599864, 0.46972, 0.545041, 0.930385, 0.828783,
           0.839992)
upper <- c(1.077371, 1.030758, 1.072965, 1.358784, 1.177753, 1.37967, 0.966411,
           0.957475)
number <- c(1239, 1529, 1682, 626, 1216, 4524, 17187, 28003)
pool <- c(0, 0, 0, 0, 0, 0, 0, -1)     # 0 an individual study, -1 the overall pool
k <- length(or)

# Limits entered the wrong way round are swapped, as StatsDirect swaps them
lo <- pmin(lower, upper)
hi <- pmax(lower, upper)
lower <- lo
upper <- hi

# The labels StatsDirect writes to the right of each row: the estimate and its limits
# to 2 decimal places (3 or 4 when the smallest value is below 0.01 or 0.001)
label <- sprintf("%.2f (%.2f, %.2f)", or, lower, upper)
cat(paste(study, label), sep = "\n")

# Base R has no forest plot function, so the chart is drawn from its parts. The rows
# follow the order of the columns from the top down; the axis is logarithmic because
# the summary statistic is a ratio (StatsDirect chooses a log scale when the column's
# title contains "ratio", and a linear scale otherwise).
y <- rev(seq_len(k))
op <- par(mar = c(5, 5, 4, 9))
plot(or, y, type = "n", log = "x", xlim = range(lower, upper), ylim = c(0.5, k + 0.5),
     yaxt = "n", ylab = "", xlab = "Odds Ratio (95% confidence interval)",
     main = "Forest plot", bty = "n")
mtext(study, side = 2, at = y, las = 1, line = 0.5)
mtext(label, side = 4, at = y, las = 1, line = 0.5)

# Each study: a grey square whose side is proportional to the square root of its
# weight relative to the heaviest study (plus a small minimum), the confidence
# interval as a line through it, and a dot at the estimate
s <- pool == 0
w <- number[s] / max(number[s])
points(or[s], y[s], pch = 15, col = "grey", cex = 0.5 + 3 * sqrt(w))
segments(lower[s], y[s], upper[s], y[s])
points(or[s], y[s], pch = 16, cex = 0.5)

# The pooled estimate: a grey diamond with its confidence interval as a line through
# it and, for the overall pool (indicator below 0), a dashed line rising from the top
# of the diamond to the first row. StatsDirect draws no line of no effect unless one
# is set as a marker line in the chart options; abline(v = 1) would add it.
p <- pool != 0
points(or[p], y[p], pch = 18, col = "grey", cex = 4)
segments(lower[p], y[p], upper[p], y[p])
segments(or[pool < 0], y[pool < 0] + 0.35, or[pool < 0], k, lty = 2)
par(op)
