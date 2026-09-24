# Block randomization: the StatsDirect help illustration (20 subjects allocated to
# 2 treatments in blocks of random size, seed 10) in R
n <- 20                                   # subjects
t <- 2                                    # treatments, labelled A, B, ...
seed <- 10
if (n %% t != 0) {
  stop("the number of subjects must be a multiple of the number of treatments")
}

# Each block holds the treatments in equal numbers, in an order drawn at random by
# sample(). StatsDirect and R each have their own Mersenne Twister generator and
# their own way of turning its output into a permutation, so the same seed gives a
# different allocation in R: the structure of the output is the same, the sequence
# is not. Keep the seed with the allocation, as either program can then repeat it.
set.seed(seed)
treatments <- LETTERS[1:t]
allocation <- character(0)
sizes <- integer(0)
while (length(allocation) < n) {
  left <- n - length(allocation)
  # a block of 2, 3 or 4 times the number of treatments, chosen at random; the
  # last block takes whatever is left once fewer than 4 times t subjects remain
  size <- if (left < 4 * t) left else t * sample(2:4, 1)
  sizes <- c(sizes, size)
  allocation <- c(allocation, sample(rep(treatments, size / t)))
}
cat("Random allocation in blocks\n")
cat("Randomized with seed:", seed, "\n")
cat("Subjects:", n, "\n")
cat("Block size: random between", 2 * t, "and", 4 * t, "\n")
cat("Treatments:", t, "\n")
print(data.frame(Subject = 1:n, Treatment = allocation), row.names = FALSE)
cat("Block sizes drawn:", sizes, "\n")
print(table(allocation))

# Blocks of one fixed size instead (4 here): n must be a multiple of the block
# size, and the block size a multiple of the number of treatments.
b <- 4
set.seed(seed)
fixed <- unlist(lapply(seq_len(n / b), function(i) sample(rep(treatments, b / t))))
cat("\nBlock size:", b, "\n")
print(data.frame(Subject = 1:n, Treatment = fixed), row.names = FALSE)
