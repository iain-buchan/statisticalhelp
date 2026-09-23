# Principal components and scale reliability: the StatsDirect help example (four
# questionnaire items answered by 60 people) in R. Save the test workbook's
# Agreement worksheet columns Question 1 to 4, with their headings, as
# questionnaire.csv in R's working directory first
q <- read.csv("questionnaire.csv")
names(q) <- paste("Question", 1:4)

# Items that load on the first component with the opposite sign to the first item
# are reversed, as the report did for questions 3 and 4, so that all the items
# point the same way (a component's sign is arbitrary, so the first item fixes
# it); then R's standard principal components analysis of the correlation matrix
loading1 <- prcomp(q, scale. = TRUE)$rotation[, 1]
reversed <- names(q)[sign(loading1) != sign(loading1[1])]
cat("Sign was reversed for:", paste(reversed, collapse = "; "), "\n")
for (item in reversed) q[[item]] <- max(q[[item]]) + min(q[[item]]) - q[[item]]
pc <- prcomp(q, scale. = TRUE)
print(summary(pc))

# The report's table to 6 places: the eigenvalues are the squared singular values
# over n - 1, with their proportions of the total variance
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
eig <- pc$sdev^2
pct <- function(x) {
  paste0(formatC(100 * x, digits = 2, format = "f", drop0trailing = TRUE), "%")
}
for (i in seq_along(eig)) {
  cat(i, six(eig[i]), pct(eig[i] / sum(eig)), pct(cumsum(eig)[i] / sum(eig)), "\n")
}

# Cronbach's alpha of the four items, from the raw scores and from the standardised
# scores, its lower confidence limit (Feldt 1965: 1 - (1 - alpha) times the 95%
# point of F on n - 1 and (n - 1)(k - 1) degrees of freedom), and the alpha with
# each item dropped
alpha <- function(x) {
  k <- ncol(x)
  k / (k - 1) * (1 - sum(apply(x, 2, var)) / var(rowSums(x)))
}
n <- nrow(q)
k <- ncol(q)
for (kind in c("raw", "standardized")) {
  x <- if (kind == "raw") q else as.data.frame(scale(q))
  a <- alpha(x)
  lower <- 1 - (1 - a) * qf(0.95, n - 1, (n - 1) * (k - 1))
  cat("With", kind, "variables: Scale reliability alpha =", six(a),
      "(95% lower confidence limit =", six(lower), ")\n")
  for (j in 1:k) {
    aj <- alpha(x[, -j])
    cat("  Question", j, "dropped:", six(aj), " change", six(aj - a), "\n")
  }
}
