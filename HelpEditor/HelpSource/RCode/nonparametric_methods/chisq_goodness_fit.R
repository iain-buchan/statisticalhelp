# Chi-square goodness of fit: the StatsDirect help example (blood groups of 187
# consecutive patients against the distribution expected in the population) in R
observed <- c(O = 67, A = 83, B = 29, AB = 8)
expected <- c(0.44, 0.45, 0.08, 0.03)     # proportions expected; they add up to 1

# R's standard test. With p given it compares the counts with those proportions;
# rescale.p = TRUE lets p be percentages or expected counts instead.
test <- chisq.test(observed, p = expected)
print(test)

# Observed and expected frequencies, and the result as StatsDirect reports it
cat("N =", sum(observed), "\n")
print(cbind("Observed frequency" = observed, "Expected frequency" = test$expected))
cat(sprintf("Chi-square = %.6f  df = %d\nP = %.4f\n", test$statistic,
            test$parameter, test$p.value))

# Like StatsDirect, R warns when the expected frequencies are small: the
# chi-square approximation is then unreliable.
