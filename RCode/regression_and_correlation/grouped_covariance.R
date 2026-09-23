# Grouped linear regression with covariance analysis: the StatsDirect help example
# (Armitage and Berry 1994, bone density of rats given three preparations of
# vitamin D at several log doses, with replicate scores at each dose; the test
# workbook's columns Log Dose_Std, BD 1_Std to BD 3_Std, Log Dose_I, BD 1_I to
# BD 5_I, Log Dose_F and BD 1_F to BD 3_F) in R
std <- list(x = c(0.544, 0.845, 1.146),
            y = list(c(0, 0, 1, 2.75, 2.75, 1.75, 2.75, 2.25, 2.25, 2.5),
                     c(1.5, 2.5, 5, 6, 4.25, 2.75, 1.5, 3),
                     c(2, 2.5, 5, 4, 5, 4, 2.5, 3.5, 3, 2, 3, 4, 4)))
prep_i <- list(x = c(0.398, 0.699, 1, 1.301, 1.602),
               y = list(c(0, 1, 0, 0, 0, 0.5), c(1, 1.5, 1.5, 1, 1, 0.5),
                        c(1.5, 1, 2, 3.5, 2, 0), c(3, 3, 5.5, 2.5, 1, 2),
                        c(3.5, 3.5, 4.5, 3.5, 3.5, 3)))
prep_f <- list(x = c(0.398, 0.699, 1),
               y = list(c(2.75, 2, 1.25, 2, 0, 0.5), c(2.5, 2.75, 2.25, 2.25, 3.75),
                        c(3.75, 5.25, 6, 5.5, 2.25, 3.5)))
groups <- list("Log Dose_Std" = std, "Log Dose_I" = prep_i, "Log Dose_F" = prep_f)

# One row per rat: its group, log dose and score
d <- do.call(rbind, lapply(names(groups), function(g) {
  data.frame(group = g, x = rep(groups[[g]]$x, lengths(groups[[g]]$y)),
             y = unlist(groups[[g]]$y))
}))
d$group <- factor(d$group, levels = names(groups))

# R's standard comparison of the regression lines: separate lines against a
# common slope (the "group:x" line is the between slopes test) and the common
# slope itself, each against the residual about the separate lines
separate <- lm(y ~ group * x, data = d)
print(anova(lm(y ~ group + x, data = d), separate))
print(anova(separate))

# The report's table to 6 places: the common slope, the difference between the
# slopes, the residual about the separate lines and the within groups total
six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}
a <- anova(separate)
ss_common <- a["x", "Sum Sq"]
ss_between <- a["group:x", "Sum Sq"]
ss_res <- a["Residuals", "Sum Sq"]
df_res <- a["Residuals", "Df"]
ms_res <- ss_res / df_res
cat("Common slope", six(ss_common), 1, six(ss_common), six(ss_common / ms_res),
    pv(pf(ss_common / ms_res, 1, df_res, lower.tail = FALSE)), "\n")
vr_between <- ss_between / 2 / ms_res
cat("Between slopes", six(ss_between), 2, six(ss_between / 2), six(vr_between),
    pv(pf(vr_between, 2, df_res, lower.tail = FALSE)), "\n")
cat("Separate residuals", six(ss_res), df_res, six(ms_res), "\n")
within <- sum(tapply(d$y, d$group, function(v) sum((v - mean(v))^2)))
cat("Within groups", six(within), nrow(d) - 3, "\n")

# Each pair of slopes: their difference with its confidence interval and t test on
# the residual mean square about the separate lines
slope <- function(g) coef(lm(y ~ x, data = d[d$group == g, ]))[2]
ssx <- function(g) with(d[d$group == g, ], sum((x - mean(x))^2))
tcrit <- qt(0.975, df_res)
for (pair in list(1:2, c(1, 3), 2:3)) {
  g1 <- names(groups)[pair[1]]
  g2 <- names(groups)[pair[2]]
  se <- sqrt(ms_res * (1 / ssx(g1) + 1 / ssx(g2)))
  dif <- abs(slope(g1) - slope(g2))
  t <- (slope(g1) - slope(g2)) / se
  cat("slope", pair[1], "(", g1, ") v slope", pair[2], "(", g2, ") =",
      six(slope(g1)), "v", six(slope(g2)), "\n")
  cat("  Difference (95% CI) =", six(dif), "(", six(dif - tcrit * se), "to",
      six(dif + tcrit * se), ")  t =", six(t), " ", pv(2 * pt(-abs(t), df_res)), "\n")
}

# The covariance analysis compares the groups at the same log dose, with the lines
# made parallel. The uncorrected table holds the sums of squares and products of
# the scores (YY), the products with log dose (xY) and the log doses (xx): between
# the groups, within them and in total, over every rat
k <- 3
n_g <- table(d$group)
sy_g <- tapply(d$y, d$group, sum)
sx_g <- tapply(d$x, d$group, sum)
syy_b <- sum(sy_g^2 / n_g) - sum(d$y)^2 / nrow(d)
sxx_b <- sum(sx_g^2 / n_g) - sum(d$x)^2 / nrow(d)
sxy_b <- sum(sx_g * sy_g / n_g) - sum(d$x) * sum(d$y) / nrow(d)
syy_t <- sum((d$y - mean(d$y))^2)
sxx_t <- sum((d$x - mean(d$x))^2)
sxy_t <- sum((d$x - mean(d$x)) * (d$y - mean(d$y)))
cat("Uncorrected: Between groups", six(syy_b), six(sxy_b), six(sxx_b), k - 1, "\n")
cat("Uncorrected: Within", six(syy_t - syy_b), six(sxy_t - sxy_b), six(sxx_t - sxx_b),
    nrow(d) - k, "\n")
cat("Uncorrected: Total", six(syy_t), six(sxy_t), six(sxx_t), nrow(d) - 1, "\n")

# The corrected table is R's comparison of the parallel lines model with a single
# line: the drop in residual sum of squares is the between groups sum of squares,
# and the parallel lines model's residual is the corrected within groups sum
common <- lm(y ~ x, data = d)
parallel <- lm(y ~ x + group, data = d)
print(anova(common, parallel))
cv <- anova(common, parallel)
css_w <- cv$RSS[2]
df_w <- cv$Res.Df[2]
css_b <- cv$"Sum of Sq"[2]
vr <- (css_b / (k - 1)) / (css_w / df_w)
cat("Corrected: Between groups", six(css_b), k - 1, six(css_b / (k - 1)), six(vr), "\n")
cat("Corrected: Within", six(css_w), df_w, six(css_w / df_w), "\n")
cat("Corrected: Total", six(cv$RSS[1]), cv$Res.Df[1], "\n")
cat(pv(pf(vr, k - 1, df_w, lower.tail = FALSE)), "\n")

# Corrected means: each group's line at the mean log dose of all the rats, with
# the standard error R gives for a prediction from the parallel lines model
mx0 <- mean(d$x)
at_mean <- predict(parallel, newdata = data.frame(x = mx0, group = names(groups)),
                   se.fit = TRUE)
cat("Corrected Y means +/- SE for baseline mean predictor of", six(mx0), ":\n")
for (g in 1:3) cat("  Y' =", six(at_mean$fit[g]), "+/-", six(at_mean$se.fit[g]), "\n")

# The vertical separations of the lines are the differences between the group
# terms of the parallel lines model (the first group is the baseline, so its
# term is zero), with their standard errors from the model's covariance matrix
bs <- coef(parallel)["x"]
cat("Line separations (common slope =", six(bs), "):\n")
term <- c(0, coef(parallel)[c("groupLog Dose_I", "groupLog Dose_F")])
v <- matrix(0, 3, 3)
v[2:3, 2:3] <- vcov(parallel)[c("groupLog Dose_I", "groupLog Dose_F"),
                              c("groupLog Dose_I", "groupLog Dose_F")]
tcrit <- qt(0.975, df_w)
for (pair in list(1:2, c(1, 3), 2:3)) {
  g1 <- pair[1]
  g2 <- pair[2]
  sep <- term[g1] - term[g2]
  se <- sqrt(v[g1, g1] + v[g2, g2] - 2 * v[g1, g2])
  cat("  line", g1, "vs line", g2, "Vertical separation =", six(sep), " 95% CI =",
      six(sep - tcrit * se), "to", six(sep + tcrit * se), " t =", six(sep / se),
      paste0("(", df_w, " df)"), pv(2 * pt(-abs(sep / se), df_w)), "\n")
}

# The report ends with a plot of the scores against log dose with the three fitted
# lines
plot(d$x, d$y, pch = as.numeric(d$group), col = as.numeric(d$group),
     xlab = "Log Dose_Std Log Dose_I Log Dose_F", ylab = "Y Replicates",
     main = "Grouped linear regression")
for (g in 1:3) abline(lm(y ~ x, data = d[d$group == names(groups)[g], ]), col = g)
legend("bottomright", legend = names(groups), pch = 1:3, col = 1:3, bty = "n")
