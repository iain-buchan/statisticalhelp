# Generalised Cochran-Mantel-Haenszel tests: the StatsDirect help example (Agresti
# 2002, job satisfaction by income in 104 people, controlling for gender) in R
# The data are the Income, Job Satisfaction and Gender columns of the Tables
# worksheet of the StatsDirect test workbook. Save those columns, with their
# headings, as job_satisfaction.csv in R's working directory first.
d <- read.csv("job_satisfaction.csv")
tab <- table(Income = d$Income, Satisfaction = d$Job.Satisfaction, Gender = d$Gender)
print(tab)                        # one income by satisfaction table per gender
u <- c(3, 10, 20, 35)             # scores for the income categories (the rows)
v <- c(1, 3, 4, 5)                # scores for the satisfaction categories (columns)

# R's mantelhaen.test gives the third of the report's tests, the generalised
# Cochran-Mantel-Haenszel test of nominal association for a stratified r by c
# table (Landis, Heyman and Koch 1978); its continuity correction applies only to
# 2 by 2 by k tables, so none is made here
print(mantelhaen.test(tab))

six <- function(x) formatC(x, digits = 6, format = "f", drop0trailing = TRUE)
pv <- function(p) {
  if (p < 0.0001) "P < 0.0001" else
    paste("P =", formatC(p, digits = 4, format = "f", drop0trailing = TRUE))
}

# All three tests are the same quadratic form (Landis et al. 1978, Agresti 2002):
# within each stratum the cell counts, stacked column by column, are compared with
# their expected values given the margins, and the differences are summed over the
# strata along with their covariance under the multiple hypergeometric distribution.
# A linear transformation A chooses what is compared: the counts themselves (nominal
# association), the row totals of the column scores (nominal rows, ordinal columns)
# or the sum of the products of the row and column scores (ordinal association).
cmh <- function(A) {
  s <- 0
  V <- 0
  for (k in seq_len(dim(tab)[3])) {
    n <- tab[, , k]
    N <- sum(n)
    if (N < 2) next                 # a stratum of one observation adds nothing
    rs <- rowSums(n)
    cs <- colSums(n)
    m <- as.vector(outer(rs, cs) / N)
    cov <- kronecker(N * diag(cs) - cs %*% t(cs), N * diag(rs) - rs %*% t(rs)) /
      (N^2 * (N - 1))
    s <- s + A %*% (as.vector(n) - m)
    V <- V + A %*% cov %*% t(A)
  }
  as.numeric(t(s) %*% solve(V) %*% s)   # solve() fails if V is singular
}
r <- dim(tab)[1]
cc <- dim(tab)[2]
drop_last <- function(k) cbind(diag(k - 1), 0)   # the last category is redundant
A_nominal <- kronecker(drop_last(cc), drop_last(r))
A_rows <- kronecker(matrix(v, 1), drop_last(r))
A_ordinal <- kronecker(matrix(v, 1), matrix(u, 1))
show <- function(label, q, df) {
  cat(label, "=", six(q), " DF =", df, " ", pv(pchisq(q, df, lower.tail = FALSE)), "\n")
}
cat("Income scores:", paste(u, collapse = ", "), "\n")
cat("Job Satisfaction scores:", paste(v, collapse = ", "), "\n")
show("Ordinal association", cmh(A_ordinal), 1)
show("Nominal rows vs. ordinal columns association", cmh(A_rows), r - 1)
show("Nominal association", cmh(A_nominal), (r - 1) * (cc - 1))
cat("Sample size =", sum(tab), "\n")
