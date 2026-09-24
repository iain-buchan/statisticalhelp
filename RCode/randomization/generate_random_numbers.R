# Random numbers by distribution: the StatsDirect help illustration (ten standard
# normal deviates from seed 1234, then samples from the other distributions that the
# Data_Generating_Random Numbers menu offers) in R
# StatsDirect and R both use the Mersenne Twister generator, but each seeds it in
# its own way, so the same seed gives a different series in each program. Within one
# program a seed always repeats its own series: set.seed() then the r* function.
set.seed(1234)
x <- rnorm(10, mean = 0, sd = 1)
cat("Normal (seed 1234, mean = 0, sd = 1), from R's generator:\n")
print(round(x, 6))
cat("Mean of the ten =", round(mean(x), 6), " sd =", round(sd(x), 6), "\n")
print(summary(x))

# The menu's other distributions and R's function for each, with StatsDirect's
# parameter names in the comment; n is the number of rows to fill. R's runif and
# StatsDirect's uniform generator both leave out the end points 0 and 1.
n <- 1000
set.seed(1234)
u01 <- runif(n)                            # Uniform 0 to 1
uab <- runif(n, min = 2, max = 5)          # Uniform A to B, interval type
cnt <- sample(1:6, n, replace = TRUE)      # Uniform A to B, count type: whole numbers
ln <- rlnorm(n, meanlog = 0, sdlog = 1)    # Lognormal: log mean, log sd
bi <- rbinom(n, size = 10, prob = 0.3)     # Binomial: number of trials, probability
po <- rpois(n, lambda = 4)                 # Poisson: mean
ga <- rgamma(n, shape = 2, scale = 3)      # Gamma: A (shape), B (scale, 1/lambda)
ex <- rexp(n, rate = 2)                    # Exponential: rate
ch <- rchisq(n, df = 3)                    # Chi-square: degrees of freedom
fv <- rf(n, df1 = 4, df2 = 10)             # F: numerator, denominator df
st <- rt(n, df = 5)                        # Student's t: degrees of freedom
lo <- rlogis(n, location = 0, scale = 1)   # Logistic: mu, sigma
ge <- rgeom(n, prob = 0.3)                 # Geometric: P (failures before a success)
nb <- rnbinom(n, size = 2, prob = 0.5)     # Negative binomial: N, P (failures before
                                           #   the Nth success)
ca <- rcauchy(n, location = 0, scale = 1)  # Cauchy: L (location), S (scale)
we <- rweibull(n, shape = 1.5, scale = 2)  # Weibull: A (shape), sigma (scale)
be <- rbeta(n, shape1 = 2, shape2 = 5)     # Beta: A, B

# A sample's summary should sit close to the distribution's mean and variance: for
# the gamma distribution A * B and A * B^2, for the lognormal exp(mu + sigma^2 / 2)
# and exp(2 * mu + 2 * sigma^2) - exp(2 * mu + sigma^2), for the Poisson its mean
cat("Uniform 0 to 1 (mean 0.5, variance 1/12):\n")
print(summary(u01))
cat("Sample variance =", round(var(u01), 4), "\n")
cat("Gamma (A = 2, B = 3; mean 6, variance 18):\n")
print(summary(ga))
cat("Sample variance =", round(var(ga), 4), "\n")
cat("Lognormal (log mean 0, log sd 1; mean", round(exp(0.5), 4), "variance",
    paste0(round(exp(2) - exp(1), 4), "):"), "\n")
print(summary(ln))
cat("Sample variance =", round(var(ln), 4), "\n")
cat("Poisson (mean 4, variance 4):\n")
print(summary(po))
cat("Sample variance =", round(var(po), 4), "\n")
cat("Binomial (10 trials, p = 0.3; mean 3, variance 2.1):\n")
print(summary(bi))
cat("Sample variance =", round(var(bi), 4), "\n")

# The gamma sample against its density
hist(ga, freq = FALSE, breaks = 30, main = "Gamma (A = 2, B = 3)", xlab = "x")
curve(dgamma(x, shape = 2, scale = 3), add = TRUE, col = "red")
