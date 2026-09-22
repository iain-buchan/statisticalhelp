# Fully nested (hierarchical) analysis of variance: the StatsDirect help example
# (Snedecor and Cochran 1989, calcium in the leaves of turnip greens: four
# plants, three leaves from each plant, two samples from each leaf) in R
calcium <- c(3.28, 3.09, 3.52, 3.48, 2.88, 2.80,
             2.46, 2.44, 1.87, 1.92, 2.19, 2.19,
             2.77, 2.66, 3.74, 3.44, 2.55, 2.55,
             3.78, 3.87, 4.07, 4.12, 3.31, 3.31)     # by plant, then leaf
plant <- factor(rep(1:4, each = 6))
leaf <- factor(rep(rep(1:3, each = 2), times = 4))

# R's standard analysis: leaves are nested within plants, written plant/leaf.
# The F ratios that summary() prints all use the residual mean square
fit <- aov(calcium ~ plant/leaf)
print(summary(fit))

# The report's table to 6 places, and its three F ratios: StatsDirect also
# tests the groups (plants) against the subgroups within groups (leaves within
# plants), the appropriate test when the leaves are a random sample
a <- anova(fit)
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else paste("P =", formatC(p, digits = 4, format = "f"))
}
ms <- a$"Mean Sq"
df <- a$Df
cat("Between Groups", six(a$"Sum Sq"[1]), df[1], six(ms[1]), "\n")
cat("Between Subgroups within Groups", six(a$"Sum Sq"[2]), df[2], six(ms[2]), "\n")
cat("Residual", six(a$"Sum Sq"[3]), df[3], six(ms[3]), "\n")
cat("Total", six(sum(a$"Sum Sq")), sum(df), "\n")
f1 <- ms[1] / ms[3]
f2 <- ms[1] / ms[2]
f3 <- ms[2] / ms[3]
p1 <- pf(f1, df[1], df[3], lower.tail = FALSE)
p2 <- pf(f2, df[1], df[2], lower.tail = FALSE)
p3 <- pf(f3, df[2], df[3], lower.tail = FALSE)
cat("F (VR between groups) =", six(f1), " ", pv(p1), "\n")
cat("F (using group/subgroup msqr) =", six(f2), " ", pv(p2), "\n")
cat("F (VR between subgroups within groups) =", six(f3), " ", pv(p3), "\n")
