# Frequencies: the StatsDirect help example (eight responses on a Likert scale)
# in R
response <- c(3, 3, 4, 1, 1, 2, 5, 3)
n <- length(response)

# R's standard table of counts
counts <- table(response)
print(counts)

# The report's table: each value with its frequency, relative frequency as a
# percentage, cumulative frequency and cumulative percentage
cat("Total =", n, "\n")
cat("Value  Frequency  Relative %  Cumulative  Cumulative Relative %\n")
cumulative <- cumsum(counts)
for (i in seq_along(counts)) {
  cat(names(counts)[i], counts[i], 100 * counts[i] / n, cumulative[i],
      100 * cumulative[i] / n, "\n")
}
