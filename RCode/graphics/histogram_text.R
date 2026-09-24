# Histogram (text-based): the StatsDirect help example (Aziz et al. 1996, the
# sperm deformity index of 116 men whose partners did not conceive; the test
# workbook's Graphics worksheet column "SDI not conceived") in R
# The data are read from sdi_not_conceived.csv: that column of the Graphics
# worksheet of the StatsDirect test workbook, saved with its heading as a csv file
# in R's working directory.
sdi <- read.csv("sdi_not_conceived.csv")[[1]]
cat("n =", length(sdi), "  range", min(sdi), "to", max(sdi), "\n")

# StatsDirect chose nine bins of width 10 with "neat" mid-points 110 to 190, so
# the bin edges are 105, 115, ..., 195. A value equal to a bin's upper limit is
# counted in that bin, which is hist()'s default (right = TRUE), and a value on the
# lowest edge would go into the first bin (include.lowest = TRUE, also the default).
edges <- seq(105, 195, by = 10)
h <- hist(sdi, breaks = edges, plot = FALSE)
print(data.frame(mid_point = h$mids, count = h$counts))

# The text histogram as StatsDirect prints it: the highest bin first, its count at
# the left, then the mid-point and a bar of "=" signs. The bars are on the scale of
# the count ruler drawn beneath them, which ends at a neat value above the largest
# count (35 here, which pretty() also gives; for other data the two may differ),
# and the full width of 60 characters stands for that value
ruler <- pretty(c(0, max(h$counts)))
bar <- strrep("=", round(h$counts / max(ruler) * 60))
# a bin with a count too small for one sign is marked with a colon
bar[h$counts > 0 & bar == ""] <- ":"
cat("Distribution of SDI not conceived\n")
cat("Counts     Mid-points\n")
for (i in rev(seq_along(h$mids)))
  cat(formatC(h$counts[i], width = -6), "|", formatC(h$mids[i], width = 4), bar[i],
      "\n")
cat("Count ruler:", ruler, "\n")
cat("Mid-points for SDI not conceived\n")

# The same bins drawn as an ordinary histogram, labelled at the bin mid-points
plot(h, main = "Distribution of SDI not conceived", ylab = "Counts",
     xlab = "Mid-points for SDI not conceived", xaxt = "n")
axis(1, at = h$mids)
