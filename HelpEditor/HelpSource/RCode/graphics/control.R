# Control chart: the StatsDirect help illustration (a process indicator recorded on
# 19 dates in 1999; the test workbook's Graphics worksheet columns Process and Date)
# in R
process <- c(23.9, 24.6, 24.3, 23.5, 23.5, 23.4, 23.6, 23.6, 22.9, 22.9, 23.6, 24.3,
             23.4, 23.5, 22.9, 21.5, 23.7, 23.8, 23.0)
date <- as.Date(c("1999-01-19", "1999-02-02", "1999-02-16", "1999-03-02", "1999-03-16",
                  "1999-03-30", "1999-04-13", "1999-04-27", "1999-05-11", "1999-05-25",
                  "1999-06-08", "1999-06-15", "1999-07-13", "1999-07-27", "1999-08-10",
                  "1999-08-17", "1999-09-14", "1999-09-21", "1999-10-05"))

# The bands are the mean and 1, 2 and 3 standard deviations either side of it, with
# the mean and sd calculated from all of the process data (StatsDirect's default
# option); sd() divides by n - 1, as StatsDirect does.
m <- mean(process)
s <- sd(process)
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
cat("Mean =", six(m), "  SD =", six(s), "  n =", length(process), "\n")

# The labels StatsDirect writes at the right hand end of each band, to its default
# 3 decimal places
k <- c(0, 1, -1, 2, -2, 3, -3)
band <- m + k * s
label <- paste0(formatC(band, digits = 3, format = "f", drop0trailing = TRUE), " (",
                c("mean", "+1 SD", "-1 SD", "+2 SD", "-2 SD", "+3 SD", "-3 SD"), ")")
cat(label, sep = "\n")

# The chart: the process values joined in date order, with the mean (black), 1 SD
# (green), 2 SD (black) and 3 SD (red) bands labelled beyond the right end of the
# axis. StatsDirect labels the date axis at calendar intervals to suit the span of
# the data, here the quarters that fall within it.
op <- par(mar = c(5, 4, 4, 7))
plot(date, process, type = "l", col = "steelblue", xaxt = "n", xlab = "Date",
     ylab = "Process", main = "Control chart", ylim = range(process, band))
axis.Date(1, at = seq(as.Date("1999-04-01"), by = "quarter", length.out = 3),
          format = "%d/%m/%Y")
abline(h = band, col = c("black", "green", "green", "black", "black", "red", "red"))
text(par("usr")[2], band, label, pos = 4, xpd = NA, cex = 0.8)
par(op)
