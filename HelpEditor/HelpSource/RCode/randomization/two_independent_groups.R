# Random allocation to two independent groups: the StatsDirect help illustration (30
# subjects, 15 to each of an intervention and a control group, seed 10) in R
n <- 30
seed <- 10

# StatsDirect shuffles the subject numbers 1 to n with its own Mersenne Twister
# generator, gives the first half to the intervention group and the second half to
# the control group, and lists each group in ascending order. In R, sample(n) draws
# a random permutation of 1 to n, and the groups are taken from it in the same way.
# R's default generator is also a Mersenne Twister, but set.seed() sets its state
# from the seed in R's own way, so for the same seed R draws a different sequence
# from StatsDirect's: the allocation printed here is another valid random
# allocation, not a copy of the one in the help. Any allocation from a stated seed
# can be reproduced by running the same code again with that seed.
set.seed(seed)
if (n %% 2 != 0) stop("the number of subjects must be even")
perm <- sample(n)
half <- n / 2
intervention <- sort(perm[1:half])
control <- sort(perm[(half + 1):n])

cat("Unpaired random allocation to intervention or control group\n")
cat("Randomized with seed: ", seed, "\n", sep = "")
cat("Subjects: ", n, " (", half, " to each group)\n", sep = "")
cat(sprintf("Intervention %2d   Control %2d", intervention, control), sep = "\n")

# Checks that the two groups partition the subjects
stopifnot(length(intervention) == half, length(control) == half)
stopifnot(identical(sort(c(intervention, control)), 1:n))
