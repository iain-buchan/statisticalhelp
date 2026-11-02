# Screening test errors (Bayes' theorem): the StatsDirect help example (a hypothetical
# screening test tried on 4000 patients, 2000 with the disease and 2000 without) in R
tp <- 1902   # a: test positive, disease present
fp <- 22     # b: test positive, disease absent
fn <- 98     # c: test negative, disease present
tn <- 1978   # d: test negative, disease absent
counts <- matrix(c(tp, fp, fn, tn), 2, byrow = TRUE,
                 dimnames = list(Test = c("Positive", "Negative"),
                                 Disease = c("Present", "Absent")))
print(addmargins(counts))

# The test's sensitivity and specificity come from the trial; the prevalence of the
# disease in the population to be screened is entered as 1 in n
sensitivity <- tp / (tp + fn)                    # P(positive test | disease)
specificity <- tn / (fp + tn)                    # P(negative test | no disease)
n <- 100
prevalence <- 1 / n                              # P(disease) before the test
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
cat("For an overall case rate of", six(prevalence * 10000),
    "per ten thousand population tested:\n")

# Base R has no function for this, so Bayes' theorem is applied directly. The false
# positive rate is the probability that a positive result is wrong, P(no disease |
# positive test), which is 1 - positive predictive value; the false negative rate is
# P(disease | negative test), which is 1 - negative predictive value
p_positive <- sensitivity * prevalence + (1 - specificity) * (1 - prevalence)
false_positive <- (1 - specificity) * (1 - prevalence) / p_positive
false_negative <- (1 - sensitivity) * prevalence / (1 - p_positive)
cat("Test SENSITIVITY = ", six(100 * sensitivity), "%\n", sep = "")
cat("Probability of a FALSE POSITIVE result =", six(false_positive), "\n")
cat("Test SPECIFICITY = ", six(100 * specificity), "%\n", sep = "")
cat("Probability of a FALSE NEGATIVE result =", six(false_negative), "\n")

# The same answers from the expected counts in 10000 people screened: 100 have the
# disease, of whom 95.1 test positive, and 9900 do not, of whom 108.9 test positive
screened <- 10000
diseased <- screened * prevalence
expected <- matrix(c(diseased * sensitivity, (screened - diseased) * (1 - specificity),
                     diseased * (1 - sensitivity), (screened - diseased) * specificity),
                   2, byrow = TRUE, dimnames = dimnames(counts))
print(addmargins(expected))
cat("False positives among the positive results =",
    six(expected["Positive", "Absent"] / sum(expected["Positive", ])), "\n")
cat("False negatives among the negative results =",
    six(expected["Negative", "Present"] / sum(expected["Negative", ])), "\n")
