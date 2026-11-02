# Randomization of intervention-control pairs: the StatsDirect help example (50
# patients, each given both treatments, allocated to the order intervention then
# control or control then intervention, seed 10, balanced allocation) in R
pairs <- 50
seed <- 10
orders <- c("Intervention - Control", "Control - Intervention")

# StatsDirect and R each have their own random number generator, so the same seed
# gives a different sequence in each program: this script repeats its own
# allocation every time it is run, but it does not repeat the table above.
# Balanced allocation gives each order to exactly half of the pairs (so the
# number of pairs must be even) in a random arrangement: sample() without
# replacement returns the 25 copies of each order in a random permutation.
set.seed(seed)
balanced <- sample(rep(orders, pairs / 2))
cat("Randomized intervention-control pairs\n")
cat(paste0("Randomized with seed: ", seed, ", balanced allocation"), "\n")
print(data.frame(pair = seq_len(pairs), order = balanced), row.names = FALSE)
cat("Pairs allocated Intervention - Control =", sum(balanced == orders[1]), "\n")
cat("Pairs allocated Control - Intervention =", sum(balanced == orders[2]), "\n")

# Every arrangement of 25 of each order is equally likely: there are choose(50, 25)
cat("Equally likely balanced arrangements =",
    formatC(choose(pairs, pairs / 2), format = "f", digits = 0), "\n")

# Without balancing, each pair's order is decided independently, like a coin
# toss, so the two orders need not be equally frequent (with this seed they happen
# to be): sample() with replacement
set.seed(seed)
tossed <- sample(orders, pairs, replace = TRUE)
cat("Unbalanced: Intervention - Control =", sum(tossed == orders[1]),
    "  Control - Intervention =", sum(tossed == orders[2]), "\n")
