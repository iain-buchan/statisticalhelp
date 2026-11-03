# Original fictional teaching examples. Numerical checks do not replace tutor review.
# Base R only; nothing is installed and no files or network services are used.
options(digits = 15)
cat(R.version.string, "\n")

# HB-PPV-01: conditional denominator for a positive test.
positive_predictive_value <- 40 / (40 + 95)
stopifnot(abs(positive_predictive_value - 8 / 27) < 1e-14)
cat("HB-PPV-01: PPV =", positive_predictive_value * 100, "%\n")

# HB-NNT-01: fictional trial, same 12-month adverse-outcome definition and complete follow-up.
intervention_risk <- 40 / 400
control_risk <- 60 / 400
absolute_risk_reduction <- control_risk - intervention_risk
relative_risk <- intervention_risk / control_risk
nnt <- 1 / absolute_risk_reduction
stopifnot(abs(absolute_risk_reduction - 0.05) < 1e-14,
          abs(relative_risk - 2/3) < 1e-14,
          abs(nnt - 20) < 1e-12)
cat("HB-NNT-01: ARR =", absolute_risk_reduction,
    "; RR =", relative_risk, "; NNT =", nnt, "over 12 months\n")

# HB-PAIRED-01: matched readings; positive differences mean lower readings afterwards.
before <- c(142, 136, 158, 149, 128, 155, 147, 139)
after <- c(139, 135, 154, 147, 123, 153, 146, 135)
d <- before - after
paired <- t.test(before, after, paired = TRUE)
differences <- t.test(d)
manual_t <- mean(d) / (sd(d) / sqrt(length(d)))
stopifnot(mean(d) == 2.75,
          abs(unname(paired$statistic) - manual_t) < 1e-13,
          isTRUE(all.equal(paired$conf.int, differences$conf.int)),
          isTRUE(all.equal(paired$p.value, differences$p.value)),
          unname(paired$parameter) == 7)
cat("HB-PAIRED-01: mean difference =", mean(d),
    "; t =", unname(paired$statistic), "; df =", unname(paired$parameter),
    "; P =", paired$p.value, "; CI =", paired$conf.int[1:2], "\n")

# HB-CASEMIX-01: rates by fictional severity stratum, not a causal provider ranking.
rates <- rbind(A = c(lower = 90/100, higher = 40/100),
               B = c(lower = 19/20, higher = 90/180))
crude <- c(A = (90 + 40)/(100 + 100), B = (19 + 90)/(20 + 180))
equal_mix <- rowMeans(rates)
stopifnot(all(rates["B", ] > rates["A", ]), crude["A"] > crude["B"],
          abs(crude["A"] - 0.65) < 1e-14,
          abs(crude["B"] - 0.545) < 1e-14,
          abs(equal_mix["A"] - 0.65) < 1e-14,
          abs(equal_mix["B"] - 0.725) < 1e-14)
cat("HB-CASEMIX-01: crude =", crude, "; equal-mix descriptive rates =", equal_mix, "\n")
cat("PASS: all four numerical checks. Educational content remains draft.\n")
