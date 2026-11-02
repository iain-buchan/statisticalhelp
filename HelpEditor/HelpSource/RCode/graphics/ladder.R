# Ladder plot: the StatsDirect help illustration (peak expiratory flow rate before
# and after a walk on a cold day in 9 asthmatics, the paired t test example; the test
# workbook's Parametric worksheet columns PEFR Before and PEFR After) in R
before <- c(312, 242, 340, 388, 296, 254, 391, 402, 290)
after <- c(300, 201, 232, 312, 220, 256, 328, 330, 231)

# StatsDirect puts the first column a quarter of the way along the x axis and the
# second three quarters of the way, a circle for each "before" value and a square for
# each "after" value, and joins each pair with a black line, the rung of the ladder.
# A pair with a blank in either column is left out. The axes are not boxed and the y
# axis has no title.
ok <- !is.na(before) & !is.na(after)
plot(c(0, 1), range(before, after, na.rm = TRUE), type = "n", xaxt = "n", bty = "l",
     xlab = "", ylab = "", main = "Ladder plot")
axis(1, at = c(0.25, 0.75), labels = c("PEFR Before", "PEFR After"))
segments(0.25, before[ok], 0.75, after[ok])
points(rep(0.25, sum(ok)), before[ok], pch = 1, col = "#40699C")
points(rep(0.75, sum(ok)), after[ok], pch = 0, col = "#9E413E")

# What the rungs show: the direction of change in each pair, and the mean difference
# that the paired t test examines
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
d <- before[ok] - after[ok]
cat("Ladder plot of PEFR Before (circles) to PEFR After (squares)\n")
cat("Pairs plotted =", length(d), "\n")
cat("The peak flow fell in", sum(d > 0), "of the", length(d), "pairs, rose in",
    sum(d < 0), "and was unchanged in", sum(d == 0), "\n")
cat("Mean of differences =", six(mean(d)), "\n")
