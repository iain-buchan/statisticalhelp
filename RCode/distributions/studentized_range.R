# Studentized range (Q) distribution: the StatsDirect help illustration (the Tukey P
# and the 5% critical value from the multiple comparisons example, and the link with
# Student's t when there are two samples) in R

# The dialog shows its results to 7 decimal places, so these do too
seven <- function(x) formatC(x, digits = 7, format = "f", drop0trailing = TRUE)

# 1. Tail probabilities of Q = 3.74767 for 4 samples (means) with 36 residual degrees
# of freedom (the Tukey contrast between Substance 1 and Substance 2): ptukey() gives
# the lower tail unless told otherwise
q <- 3.74767
k <- 4
df <- 36
upper <- ptukey(q, nmeans = k, df = df, lower.tail = FALSE)
lower <- ptukey(q, nmeans = k, df = df)
cat("P(Q 3.74767, df 36, samples 4) =", seven(upper), "upper, ", seven(lower),
    "lower\n")

# 2. The inverse: the Q with 5% in its upper tail (the 5% critical value) for 4
# samples with 36 degrees of freedom. qtukey() is documented as accurate to the 4th
# decimal place, so its value is refined to the 7 places shown by solving
# ptukey() = 0.95
crit <- qtukey(0.95, nmeans = k, df = df)
crit <- uniroot(function(x) ptukey(x, nmeans = k, df = df) - 0.95,
                c(crit - 0.05, crit + 0.05), tol = 1e-10)$root
cat("Q(upper P 0.05, df 36, samples 4) =", seven(crit), "\n")

# 3. With two samples the range of the means is their difference, so Q is root 2
# times |t|: the 5% point of Q for 2 samples with 36 degrees of freedom is root 2 times
# the two sided 5% point of Student's t on 36 degrees of freedom (2.5% in each tail)
crit2 <- qtukey(0.95, nmeans = 2, df = df)
crit2 <- uniroot(function(x) ptukey(x, nmeans = 2, df = df) - 0.95,
                 c(crit2 - 0.05, crit2 + 0.05), tol = 1e-10)$root
t <- qt(0.975, df = df)
cat("Q(upper P 0.05, df 36, samples 2) =", seven(crit2), "\n")
cat("Student's t (two sided 5% point, df 36) =", seven(t), "  times root 2 =",
    seven(sqrt(2) * t), "\n")
