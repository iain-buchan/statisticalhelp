# Preference group allocation: the StatsDirect help example (ten students ranking
# five courses; the test workbook's Other worksheet columns Group capacity and 1st,
# 2nd and 3rd choice) in R
capacity <- c(2, 1, 2, 3, 5)                       # places in groups 1 to 5
choice <- cbind(c(3, 3, 5, 3, 5, 5, 2, 4, 3, 1),   # each student's 1st choice
                c(5, 4, 1, 1, 3, 4, 3, 1, 5, 3),   # 2nd choice
                c(1, 2, 3, 4, 1, 3, 5, 5, 1, 5))   # 3rd choice
groups <- length(capacity)
subjects <- nrow(choice)
stopifnot(sum(capacity) >= subjects, choice >= 1, choice <= groups)

# R has no function for this, so the procedure is written out. Round by round (1st
# choices, then 2nd, then 3rd) each group with places left takes the unallocated
# subjects who chose it in that round; when more want it than there are places, a
# random sample of them gets in, so each has the same chance. Subjects still without
# a group after the last round are placed at random in the places left over.
# set.seed() makes an allocation repeatable. R's random number generator is not
# StatsDirect's (both are Mersenne twisters, but seeded and used differently), so the
# same seed gives a different allocation in R from the one in the help.
set.seed(10)
group <- rep(NA_integer_, subjects)
for (round in seq_len(ncol(choice))) {
  for (g in seq_len(groups)) {
    space <- capacity[g] - sum(group == g, na.rm = TRUE)
    wanting <- which(is.na(group) & choice[, round] == g)
    if (space > 0 && length(wanting) > 0) {
      taken <- if (length(wanting) > space) sample(wanting, space) else wanting
      group[taken] <- g
    }
  }
}
left <- which(is.na(group))
if (length(left) > 0) {
  spare <- rep(seq_len(groups), capacity - tabulate(group, groups))  # one entry a place
  group[left] <- spare[sample.int(length(spare), length(left))]    # even if one is left
}

# The report: the allocation, and (not in the report) which choice each student got
cat("Random allocation to groups by preference\n")
cat("Groups =", groups, "\n")
cat("Total group capacity =", sum(capacity), "\n")
cat("Subjects =", subjects, "\n")
cat("Randomized with seed: 10\n")
got <- apply(choice == group, 1, function(hit) if (any(hit)) which(hit)[1] else NA)
print(data.frame(Subject = seq_len(subjects), Group = group, Choice = got),
      row.names = FALSE)
cat("Places used:", paste(tabulate(group, groups), "of", capacity, collapse = ", "),
    "\n")
