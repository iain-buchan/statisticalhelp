# Correlation of all pairs: an illustration with the four questionnaire items of
# the principal components topic in R. The data are the test workbook's Agreement
# worksheet columns Question 1 to 4.
q <- data.frame(
  q1 = c(2, 5, 4, 4, 3, 3, 3, 4, 4, 4, 4, 5, 4, 4, 4, 3, 4, 5, 5, 4, 4, 4, 3, 5,
    5, 3, 4, 3, 4, 4, 4, 4, 4, 4, 4, 3, 4, 3, 4, 3, 4, 5, 4, 4, 4, 3, 5, 4, 3,
    5, 5, 4, 2, 3, 4, 4, 4, 3, 4, 4),
  q2 = c(3, 5, 5, 3, 3, 3, 4, 4, 5, 4, 4, 5, 4, 3, 4, 3, 5, 5, 5, 4, 4, 4, 4, 3,
    5, 3, 4, 3, 4, 4, 4, 5, 4, 5, 4, 3, 3, 4, 4, 4, 5, 5, 4, 4, 4, 4, 5, 5, 4,
    3, 3, 5, 5, 4, 3, 4, 4, 4, 4, 4),
  q3 = c(5, 4, 5, 4, 5, 4, 4, 5, 5, 3, 5, 4, 4, 5, 5, 4, 4, 5, 4, 4, 4, 4, 5, 5,
    3, 4, 4, 5, 3, 5, 5, 5, 5, 5, 5, 4, 5, 5, 5, 4, 5, 5, 5, 4, 5, 4, 5, 4, 4,
    4, 4, 4, 5, 5, 5, 4, 5, 4, 5, 5),
  q4 = c(5, 4, 5, 4, 5, 5, 4, 5, 5, 3, 5, 4, 4, 5, 5, 5, 4, 5, 4, 4, 4, 4, 5, 5,
    3, 4, 4, 5, 3, 5, 5, 5, 5, 5, 5, 4, 4, 5, 4, 4, 5, 5, 5, 4, 5, 4, 5, 4, 4,
    4, 4, 4, 5, 5, 5, 4, 5, 4, 4, 5))
names(q) <- paste("Question", 1:4)

# The principal components function had reversed questions 3 and 4 so that all
# the items point the same way, and its correlation matrix is of the items as
# reversed
for (item in c("Question 3", "Question 4")) {
  q[[item]] <- max(q[[item]]) + min(q[[item]]) - q[[item]]
}

# R's correlation matrix (Pearson's product moment) of every pair of columns, to
# 6 places as the report prints it
print(round(cor(q), 6))
