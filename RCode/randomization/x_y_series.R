# Randomizing a series of integers: the StatsDirect help illustration (the numbers
# 1 to 49, as in a lottery, put in a random order with seed 10) in R
x <- 1
y <- 49
seed <- 10

# sample(x:y) returns the integers x to y in a random order, every order being
# equally likely. StatsDirect and R each have their own Mersenne Twister generator
# and their own way of turning its output into a random order, so the same seed
# gives a different series in R: the two columns of the output are the same, the
# numbers are not. Keep the seed with the series, as either program can then repeat it.
# (For a series of one number, x = y, sample(x:y) would instead shuffle 1 to x.)
set.seed(seed)
series <- sample(x:y)
cat("Random allocation of numbers in a series\n")
cat("Randomized with seed:", seed, "\n")
print(data.frame(position = seq_along(series), number = series), row.names = FALSE)

# Six lottery numbers: the first six of the series, or equivalently sample(x:y, 6),
# which draws six of the numbers without replacement
cat("The first six:", head(series, 6), "\n")

# The five cards labelled 6 to 10
set.seed(seed)
cat("Cards 6 to 10 shuffled:", sample(6:10), "\n")
