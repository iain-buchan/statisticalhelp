# Regression check for the R code shown in the help.
#
# Each worked example has two files under RCode/, in the same folder layout as Content/:
#   <topic>.R       the tested script, exactly as shown in the topic's "R code" section
#   <topic>.expect  one fragment per line (for example "U = 243") that must appear
#                   (1) in the script's output, (2) in the help topic's text and, when a
#                   folder of program reports is given, (3) in StatsDirect's own report.
# Matching ignores case and runs of white space. Lines starting with # are comments.
# A fragment that the help or the report words differently can be limited to some checks by
# a prefix: any of the letters R (script output), H (help) and P (program report), as in "RH:" or "HP:".
#
# usage: Rscript --vanilla RCode/check-rcode.R [folder of program reports as .txt] [pattern of the scripts to check]

args <- commandArgs(trailingOnly = TRUE)
reports <- if (length(args) >= 1 && nzchar(args[1])) args[1] else NA
only <- if (length(args) >= 2) args[2] else NA   # an optional pattern: check only the scripts whose path matches it
root <- normalizePath(file.path(dirname(sub("^--file=", "", grep("^--file=",
          commandArgs(), value = TRUE)[1])), ".."))
squash <- function(s) tolower(gsub("[[:space:]]+", " ", paste(s, collapse = " ")))
topic_text <- function(path) {
  s <- paste(readLines(path, warn = FALSE, encoding = "UTF-8"), collapse = " ")
  s <- sub("<MadCap:dropDown class=\"rcode\".*?</MadCap:dropDown>", " ", s, perl = TRUE)
  s <- gsub("<[^>]+>", "", s)
  s <- gsub("&#160;|&nbsp;", " ", s)
  s <- gsub("&lt;", "<", s); s <- gsub("&gt;", ">", s); s <- gsub("&amp;", "&", s)
  squash(s)
}

scripts <- list.files(file.path(root, "RCode"), pattern = "\\.R$", recursive = TRUE)
scripts <- setdiff(scripts, "check-rcode.R")
if (!is.na(only)) scripts <- grep(only, scripts, value = TRUE)
failures <- 0
for (rel in scripts) {
  stem <- sub("\\.R$", "", rel)
  expect_file <- file.path(root, "RCode", paste0(stem, ".expect"))
  topic_file <- file.path(root, "Content", paste0(stem, ".htm"))
  # scripts run from RCode/data, where the few examples that read a data file find it
  old <- setwd(file.path(root, "RCode", "data"))
  out <- suppressWarnings(system2(file.path(R.home("bin"), "Rscript"),
           c("--vanilla", shQuote(file.path(root, "RCode", rel))), stdout = TRUE, stderr = TRUE))
  setwd(old)
  unlink(file.path(root, "RCode", "data", "Rplots.pdf"))
  texts <- list(R = squash(out), H = topic_text(topic_file))
  report_file <- if (is.na(reports)) NA else file.path(reports, paste0(gsub("/", "_", stem), ".txt"))
  if (!is.na(report_file) && file.exists(report_file))
    texts$P <- squash(readLines(report_file, warn = FALSE))
  # the code in the topic must be the tested script
  topic_raw <- paste(readLines(topic_file, warn = FALSE, encoding = "UTF-8"), collapse = "\n")
  shown <- regmatches(topic_raw, regexpr("(?s)<pre class=\"rcode\"[^>]*>.*?</pre>", topic_raw, perl = TRUE))
  shown <- gsub("<[^>]+>", "", shown)
  shown <- gsub("&lt;", "<", shown); shown <- gsub("&gt;", ">", shown); shown <- gsub("&amp;", "&", shown)
  same <- length(shown) == 1 &&
    identical(squash(shown), squash(readLines(file.path(root, "RCode", rel), warn = FALSE)))
  bad <- if (same) character() else "the code shown in the topic differs from the script"
  for (line in readLines(expect_file, warn = FALSE)) {
    line <- trimws(line)
    if (line == "" || startsWith(line, "#")) next
    where <- c("R", "H", "P")
    if (grepl("^[RHP]+:", line)) {
      where <- strsplit(sub(":.*$", "", line), "")[[1]]
      line <- trimws(sub("^[A-Z]+:", "", line))
    }
    for (w in intersect(where, names(texts)))
      if (!grepl(squash(line), texts[[w]], fixed = TRUE))
        bad <- c(bad, sprintf("[%s] missing: %s", c(R = "R output", H = "help", P = "program")[w], line))
  }
  cat(sprintf("%-55s %s\n", stem, if (length(bad)) "FAIL" else
      sprintf("ok (%s)", paste(names(texts), collapse = "+"))))
  if (length(bad)) { cat(paste0("    ", bad, "\n"), sep = ""); failures <- failures + 1 }
}
cat(sprintf("%d script(s), %d failing\n", length(scripts), failures))
quit(status = if (failures) 1 else 0)
