# Histogram (text-based): the StatsDirect help example (Aziz et al. 1996, the
# sperm deformity index of 116 men whose partners did not conceive; the test
# workbook's Graphics worksheet column "SDI not conceived") in R
sdi <- c(165, 140, 154, 139, 134, 154, 120, 133, 150, 146, 140, 114, 128, 131,
  116, 128, 122, 129, 145, 117, 140, 149, 116, 147, 125, 149, 129, 157, 144,
  123, 107, 129, 152, 164, 134, 120, 148, 151, 149, 138, 159, 169, 137, 151,
  141, 145, 135, 135, 153, 125, 159, 148, 142, 130, 111, 140, 136, 142, 139,
  137, 187, 154, 151, 149, 148, 157, 159, 143, 124, 141, 114, 136, 110, 129,
  145, 132, 125, 149, 146, 138, 151, 147, 154, 147, 158, 156, 156, 128, 151,
  138, 193, 131, 127, 129, 120, 159, 147, 159, 156, 143, 149, 160, 126, 136,
  150, 136, 151, 140, 145, 140, 134, 140, 138, 144, 140, 140)
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
