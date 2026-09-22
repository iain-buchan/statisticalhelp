# Latin square analysis of variance: the StatsDirect help example (Armitage and
# Berry 1994, blistering of rabbits' skin at six sites in six rabbits, with the
# order of inoculation as the Latin factor) in R
blister <- c(7.9, 6.1, 7.5, 6.9, 6.7, 7.3, 8.7, 8.2, 8.1, 8.5, 9.9, 8.3,
             7.4, 7.7, 6.0, 6.8, 7.3, 7.3, 7.4, 7.1, 6.4, 7.7, 6.4, 5.8,
             7.1, 8.1, 6.2, 8.5, 6.4, 6.4, 8.2, 5.9, 7.5, 8.5, 7.3, 7.7)
rabbit <- factor(rep(1:6, each = 6))              # the columns of the square
position <- factor(rep(1:6, times = 6))           # the rows
order <- factor(c(3, 4, 1, 6, 2, 5, 5, 2, 3, 1, 4, 6, 4, 6, 5, 3, 1, 2,
                  1, 5, 6, 2, 3, 4, 6, 3, 2, 4, 5, 1, 2, 1, 4, 5, 6, 3))

# R's standard analysis: the three factors are orthogonal in a Latin square, so
# each sum of squares is the same whatever the order of the terms
fit <- aov(blister ~ position + rabbit + order)
print(summary(fit))

# The report's lines to 6 places
a <- anova(fit)
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else paste("P =", formatC(p, digits = 4, format = "f"))
}
cat("Rows", six(a$"Sum Sq"[1]), a$Df[1], six(a$"Mean Sq"[1]), "\n")
cat("Columns", six(a$"Sum Sq"[2]), a$Df[2], six(a$"Mean Sq"[2]), "\n")
cat("Treatments", six(a$"Sum Sq"[3]), a$Df[3], six(a$"Mean Sq"[3]), "\n")
cat("Residual", six(a$"Sum Sq"[4]), a$Df[4], six(a$"Mean Sq"[4]), "\n")
cat("Total", six(sum(a$"Sum Sq")), sum(a$Df), "\n")
cat("F (rows) =", six(a$"F value"[1]), " ", pv(a$"Pr(>F)"[1]), "\n")
cat("F (columns) =", six(a$"F value"[2]), " ", pv(a$"Pr(>F)"[2]), "\n")
cat("F (treatments) =", six(a$"F value"[3]), " ", pv(a$"Pr(>F)"[3]), "\n")
