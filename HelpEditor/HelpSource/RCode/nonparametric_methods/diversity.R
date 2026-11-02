# Diversity of classes: the StatsDirect help example (strains of Staphylococcus
# aureus in hospital samples, Grundmann et al. 2001) in R
counts <- c(30, 13, 9, 8, 7, 7, 7, 6, 6, 5, 2, 2, 2, rep(1, 13))
N <- sum(counts)
S <- length(counts)                       # classes observed (richness)
p <- counts / N
z <- qnorm(0.975)
cat("Total number of counts =", N, "  Number of classes observed =", S, "\n")

# R has no standard function for these indices; each is a line or two.
# Estimated total number of classes (Chao 1987), from the numbers of classes
# seen once (f1) and twice (f2), with a log-normal confidence interval that
# cannot fall below the number observed
f1 <- sum(counts == 1)
f2 <- sum(counts == 2)
extra <- f1^2 / (2 * f2)
se <- sqrt(f2 * ((f1 / f2)^4 / 4 + (f1 / f2)^3 + (f1 / f2)^2 / 2))
K <- exp(z * sqrt(log(1 + se^2 / extra^2)))
cat(sprintf("Estimated total number of classes = %.0f   Standard error = %.6f\n",
            S + extra, se))
cat(sprintf("Log-normal (Chao) 95%% CI = %.0f to %.0f\n", S + extra / K,
            S + extra * K))

# Simpson's index as Hurlbert's PIE: the chance that two individuals taken at
# random belong to different classes
l <- sum(counts * (counts - 1)) / (N * (N - 1))       # dominance
Ds <- 1 - l
seL <- sqrt(4 / N * (sum(p^3) - sum(p^2)^2))
seS <- sqrt((4 * N * (N - 1) * (N - 2) * sum(p^3) + 2 * N * (N - 1) * sum(p^2) -
             2 * N * (N - 1) * (2 * N - 3) * sum(p^2)^2) / (N * (N - 1))^2)
cat(sprintf("Simpson Ds = %.6f  (dominance l = %.6f, ds = %.6f)\n", Ds, l, 1 / l))
cat(sprintf("Standard error: large sample = %.6f   small sample = %.6f\n", seL, seS))
cat(sprintf("Normal (large sample) 95%% CI = %.6f to %.6f\n", Ds - z * seL,
            Ds + z * seL))

# Shannon's index
H <- -sum(p * log(p))
hL <- sqrt((sum(p * log(p)^2) - H^2) / N)
hS <- sqrt(hL^2 + (S - 1) / (2 * N^2))
cat(sprintf("Shannon H' (base e) = %.6f\n", H))
cat(sprintf("Standard error: large sample = %.6f   small sample = %.6f\n", hL, hS))
cat(sprintf("Normal (large sample) 95%% CI = %.6f to %.6f\n", H - z * hL, H + z * hL))

# Bootstrap: draw N individuals again and again from the observed proportions.
# The results depend on the random numbers, so they differ a little from run to
# run and from StatsDirect's.
set.seed(1)
boot <- replicate(2000, {
  b <- as.vector(rmultinom(1, N, p))
  b <- b[b > 0]
  c(1 - sum(b * (b - 1)) / (N * (N - 1)), -sum(b / N * log(b / N)))
})
cat(sprintf("Bootstrap, Simpson: bias = %.6f  standard error = %.6f\n",
            Ds - mean(boot[1, ]), sd(boot[1, ])))
cat(sprintf("Bootstrap, Shannon: bias = %.6f  standard error = %.6f\n",
            H - mean(boot[2, ]), sd(boot[2, ])))
