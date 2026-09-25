# One way analysis of variance: the StatsDirect help example (Armitage and Berry
# 1994, worms recovered from four groups of rats) in R
expt1 <- c(279, 338, 334, 198, 303)
expt2 <- c(378, 275, 412, 265, 286)
expt3 <- c(172, 335, 335, 282, 250)
expt4 <- c(381, 346, 340, 471, 318)
worms <- c(expt1, expt2, expt3, expt4)
group <- factor(rep(c("Expt 1", "Expt 2", "Expt 3", "Expt 4"), each = 5))

# R's standard analysis gives the same table: its "group" line is Between
# Groups and its "Residuals" line is Within Groups
fit <- aov(worms ~ group)
print(summary(fit))

# The report's lines to 6 places
a <- anova(fit)
six <- function(x) {
  formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
}
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
cat("Between Groups", six(a$"Sum Sq"[1]), a$Df[1], six(a$"Mean Sq"[1]), "\n")
cat("Within Groups", six(a$"Sum Sq"[2]), a$Df[2], six(a$"Mean Sq"[2]), "\n")
cat("Corrected Total", six(sum(a$"Sum Sq")), sum(a$Df), "\n")
cat("F (variance ratio) =", six(a$"F value"[1]), " ", pv(a$"Pr(>F)"[1]), "\n")

# The technical validation example: the NIST SiRstv data (five instruments), to
# 12 places as the help shows them. The twelfth decimal place of F is at the
# limit of double precision for these data, as the topic notes.
inst <- c(196.3052, 196.1240, 196.1890, 196.2569, 196.3403,
          196.3042, 196.3825, 196.1669, 196.3257, 196.0422,
          196.1303, 196.2005, 196.2889, 196.0343, 196.1811,
          196.2795, 196.1748, 196.1494, 196.1485, 195.9885,
          196.2119, 196.1051, 196.1850, 196.0052, 196.2090)
instrument <- factor(rep(1:5, each = 5))
b <- anova(aov(inst ~ instrument))
twelve <- function(x) formatC(x, digits = 12, format = "f", drop0trailing = TRUE)
cat("Between Groups", twelve(b$"Sum Sq"[1]), b$Df[1], twelve(b$"Mean Sq"[1]), "\n")
cat("Within Groups", twelve(b$"Sum Sq"[2]), b$Df[2], twelve(b$"Mean Sq"[2]), "\n")
cat("Corrected Total", twelve(sum(b$"Sum Sq")), sum(b$Df), "\n")
cat("F (variance ratio) =", twelve(b$"F value"[1]), " ", pv(b$"Pr(>F)"[1]), "\n")
