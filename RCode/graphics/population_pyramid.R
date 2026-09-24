# Population pyramid: the StatsDirect help illustration (the resident population of
# the UK at mid-1998, in thousands, by sex and five-year age band; the test workbook's
# Graphics worksheet columns UK Mid-98 Age Bands, UK Mid-98 Males and UK Mid-98
# Females) in R
band <- c("0-4", "15-19", "20-24", "25-29", "30-34", "35-39", "40-44", "45-49", "50-54",
          "55-59", "60-64", "65-69", "70-74", "75-79", "80-84", "85-89", "90 and over")
males <- c(1882.1, 1884.2, 1803.2, 2251.8, 2469, 2315.2, 1979.2, 1916.3, 1954.8, 1516.2,
           1379.7, 1237.6, 1052.7, 809.8, 427.2, 217.9, 78.7)
females <- c(1788.6, 1785.4, 1717.2, 2138.3, 2372.5, 2247, 1963.9, 1917.7, 1968.2,
             1547.1, 1438.3, 1379.4, 1294.8, 1180, 787.9, 524.5, 301)

# StatsDirect draws a bar for each age band, in worksheet order from the top, with the
# males' count to the left of a centre line and the females' count to the right. Both
# halves run from 0 at the centre to the scale maximum at the edge, and there is no
# axis: the scale maximum is written under the chart. Its default is the largest count
# rounded up to a neat value (the top label of the axis StatsDirect would draw for 0 to
# that count), or the largest count itself when that is greater. The default is 2500
# here, which pretty() also gives. Give pyramids that are to be compared the same
# scale maximum.
scale_max <- max(pretty(c(0, males, females)))
cat("Scale maximum =", scale_max, "\n")

# Base R has no pyramid function: an empty plot with no axes, then rect() draws the
# bars of a horizontal bar chart back to back, blue for males and magenta for females
# as StatsDirect colours columns titled "Males" and "Females".
n <- length(band)
top <- n:1   # the row of each band, counted from the bottom, so the first is at the top
op <- par(mar = c(4, 7, 3, 1))
plot(NA, xlim = c(-scale_max, scale_max), ylim = c(0, n), axes = FALSE, xlab = "",
     ylab = "", main = "Population pyramid")
rect(-males, top - 1, 0, top, col = "blue")
rect(0, top - 1, females, top, col = "magenta")
segments(0, 0, 0, n)
mtext(band, side = 2, at = top - 0.5, las = 1, line = 0.5)
mtext(c("male", "female"), side = 1, at = c(-scale_max, scale_max) / 2, line = 0.5)
mtext(paste("Scale maximum =", scale_max), side = 1, line = 2.5, adj = 0)
par(op)

# A pyramid from a single column of totals (the workbook's UK Mid-98 Persons) is split
# evenly about the centre line: rect(-total / 2, top - 1, total / 2, top).

# The values plotted
cat("UK Mid-98 Age Bands   UK Mid-98 Males   UK Mid-98 Females\n")
for (i in seq_len(n)) {
  cat(sprintf("%-21s %15.1f %19.1f\n", band[i], males[i], females[i]))
}

# What the chart shows, in thousands
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
cat("Males =", six(sum(males)), "  Females =", six(sum(females)), "\n")
cat("Largest band:", band[which.max(males + females)], "\n")
cat("Females outnumber males from the", band[females > males][1], "band upwards\n")
ratio <- formatC(females[n] / males[n], digits = 2, format = "f")
cat("At 90 and over there are", ratio, "women for each man\n")
