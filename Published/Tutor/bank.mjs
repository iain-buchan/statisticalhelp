// Generated StatsDirect built-in tutor source. Edit the authoring course, then publish again.
export const bankVersion = "2026-11-15-biostatistics-cc2d92a4dc90";
export const tracks = {
  "foundation": {
    "title": "Medical undergraduate",
    "description": "Understand the question, choose the method, interpret the result."
  },
  "medicine": {
    "title": "Postgraduate medicine",
    "description": "Interpret diagnostic evidence, treatment effects and uncertainty."
  },
  "publicHealth": {
    "title": "Public health postgraduate",
    "description": "Work with population comparisons, bias and study design."
  },
  "analyst": {
    "title": "Healthcare / business analyst",
    "description": "Describe services, compare performance and communicate uncertainty."
  },
  "researcher": {
    "title": "Researcher / degree module",
    "description": "Choose methods, check assumptions and interpret research evidence."
  }
};
export const questions = [
  {
    "id": "UG-SENS-01",
    "version": 1,
    "track": "foundation",
    "topic": "Sensitivity",
    "objective": "Choose the denominator for sensitivity.",
    "stem": "In a fictional diagnostic study, 90 people have the condition and 810 do not. The test is positive in 72 of the 90 people with the condition and in 81 of the 810 without it. What is the sensitivity?",
    "options": [
      {
        "id": "A",
        "text": "20%",
        "feedback": "This is the false-negative proportion: 18/90."
      },
      {
        "id": "B",
        "text": "47.1%",
        "feedback": "This is the positive predictive value: 72/(72\u002B81)."
      },
      {
        "id": "C",
        "text": "80%",
        "feedback": "Use the people with the condition as the denominator: 72/90."
      },
      {
        "id": "D",
        "text": "90%",
        "feedback": "This is the specificity: 729/810."
      },
      {
        "id": "E",
        "text": "97.6%",
        "feedback": "This is the negative predictive value: 729/(729\u002B18)."
      }
    ],
    "correct": "C",
    "hint": "Start with the people known to have the condition. How many did the test detect?",
    "lesson": "Sensitivity asks: among people with the condition, what proportion test positive? Positive predictive value reverses the question: among people with a positive result, what proportion have the condition?",
    "explanation": "Sensitivity = true positives / (true positives \u002B false negatives) = 72/90 = 80%. The 81 false positives matter for specificity and predictive values, not for this denominator.",
    "calculation": {
      "kind": "sensitivity",
      "tp": 72,
      "fn": 18,
      "expected": 0.8
    },
    "help": "clinical_epidemiology/diagnostic.htm",
    "operation": "MiscDiagnostic"
  },
  {
    "id": "UG-NNT-01",
    "version": 2,
    "track": "foundation",
    "topic": "Absolute benefit and NNT",
    "objective": "Calculate NNT and retain the follow-up period.",
    "stem": "With complete one-year follow-up, an individually randomised trial records at least one unplanned admission in 16 of 200 programme participants and 28 of 200 usual-care participants. Using these point estimates, what is the number needed to treat to prevent one additional person having an admission over one year, rounded up to a whole person?",
    "options": [
      {
        "id": "A",
        "text": "6",
        "feedback": "Six percentage points is the absolute risk reduction, not its reciprocal."
      },
      {
        "id": "B",
        "text": "7",
        "feedback": "The absolute reduction is 0.06, so its reciprocal is larger than 7."
      },
      {
        "id": "C",
        "text": "12",
        "feedback": "Twelve is the difference in event counts, not NNT."
      },
      {
        "id": "D",
        "text": "17",
        "feedback": "1/(0.14\u22120.08) = 16.67, rounded up to 17."
      },
      {
        "id": "E",
        "text": "43",
        "feedback": "About 43% is the relative risk reduction; NNT uses absolute risk reduction."
      }
    ],
    "correct": "D",
    "hint": "Calculate the two event risks, subtract them, then take the reciprocal of that absolute difference.",
    "lesson": "NNT connects an absolute reduction in risk to the number of people treated. It depends on baseline risk, the outcome and the follow-up period. Count people with an outcome, not repeated events. A point estimate alone does not describe uncertainty or establish benefit.",
    "explanation": "Usual-care risk is 28/200 = 0.14. Programme risk is 16/200 = 0.08. The absolute reduction is 0.06, so NNT = 16.67, conventionally rounded up to 17, for this outcome over one year.",
    "calculation": {
      "kind": "nnt",
      "controlEvents": 28,
      "controlN": 200,
      "treatmentEvents": 16,
      "treatmentN": 200,
      "expected": 17
    },
    "help": "clinical_epidemiology/nnt.htm",
    "operation": "MiscNumberNeededToTreat"
  },
  {
    "id": "UG-PVAL-01",
    "version": 1,
    "track": "foundation",
    "topic": "P values",
    "objective": "Interpret a P value conditional on a null model.",
    "stem": "A prespecified two-sided analysis gives P = 0.03. Which interpretation is most accurate?",
    "options": [
      {
        "id": "A",
        "text": "The null hypothesis has a 3% probability of being true.",
        "feedback": "A P value is not a posterior probability for the null hypothesis."
      },
      {
        "id": "B",
        "text": "If the null model and its assumptions hold, a test statistic at least as extreme as this has probability 3%.",
        "feedback": "This is the repeated-sampling interpretation under the specified null model."
      },
      {
        "id": "C",
        "text": "The treatment has a 97% probability of working.",
        "feedback": "A P value does not give the probability that a treatment works."
      },
      {
        "id": "D",
        "text": "The estimated treatment effect is clinically important.",
        "feedback": "Clinical importance depends on the effect size, uncertainty, harms and context."
      },
      {
        "id": "E",
        "text": "There is a 3% probability that the study contains any bias.",
        "feedback": "P values do not measure the probability of bias."
      }
    ],
    "correct": "B",
    "hint": "Which statement starts by assuming the null model, rather than assigning a probability to it?",
    "lesson": "A P value measures compatibility with a specified null model using a chosen test statistic. It does not determine clinical importance or rule out bias. Read it alongside the effect estimate and confidence interval.",
    "explanation": "The probability refers to possible test statistics under the null model and its assumptions. It is not the probability that the null is true, that the finding is a chance accident, or that the effect matters clinically.",
    "help": "basics/p_values.htm",
    "operation": null
  },
  {
    "id": "UG-PAIR-01",
    "version": 1,
    "track": "foundation",
    "topic": "Choosing a paired analysis",
    "objective": "Match a comparison to its paired structure.",
    "stem": "Eighteen independently sampled adults each have a measurement before and after an intervention. Their within-person differences are approximately normally distributed, with no major outliers. Which test directly assesses whether the population mean change is zero?",
    "options": [
      {
        "id": "A",
        "text": "Unpaired t test",
        "feedback": "Treating the observations as independent loses the within-person pairing."
      },
      {
        "id": "B",
        "text": "Pearson chi-square test",
        "feedback": "This is not a comparison of categorical counts."
      },
      {
        "id": "C",
        "text": "Mann\u2013Whitney test",
        "feedback": "This test is for independent groups, not paired observations."
      },
      {
        "id": "D",
        "text": "Paired t test",
        "feedback": "The paired t test is a one-sample t test of the within-person differences."
      },
      {
        "id": "E",
        "text": "Pearson correlation test",
        "feedback": "Correlation measures association, not whether the mean change is zero."
      }
    ],
    "correct": "D",
    "hint": "What single value could you compute for each participant?",
    "lesson": "Subtract each person\u2019s before value from their after value, using the same direction for everyone. The paired t test operates on those differences. The normality assumption concerns the differences.",
    "explanation": "Use a paired t test on the 18 within-person differences. A before\u2013after change alone does not establish that the intervention caused it; a causal claim needs an appropriate study design.",
    "help": "parametric_methods/paired_t.htm",
    "operation": "TPaired"
  },
  {
    "id": "PG-PPV-01",
    "version": 1,
    "track": "medicine",
    "topic": "Predictive value and prevalence",
    "objective": "Combine prevalence, sensitivity and specificity.",
    "stem": "A fictional screening test has sensitivity 90% and specificity 95%. Assume these apply in a population with prevalence 1%. Approximately what proportion of positive results would be true positives?",
    "options": [
      {
        "id": "A",
        "text": "1%",
        "feedback": "Prevalence is the probability before seeing the test result."
      },
      {
        "id": "B",
        "text": "5%",
        "feedback": "This is the false-positive rate among people without the condition."
      },
      {
        "id": "C",
        "text": "15.4%",
        "feedback": "For 10,000 people, expect 90 true positives and 495 false positives: 90/585."
      },
      {
        "id": "D",
        "text": "90%",
        "feedback": "Sensitivity conditions on having the condition, not on a positive test."
      },
      {
        "id": "E",
        "text": "95%",
        "feedback": "Specificity conditions on not having the condition."
      }
    ],
    "correct": "C",
    "hint": "Imagine 10,000 people. Work out true positives and false positives separately.",
    "lesson": "At low prevalence, people without the condition greatly outnumber those with it. Even a modest false-positive rate can therefore produce many false positives. Keep sensitivity and predictive value separate.",
    "explanation": "Among 10,000 people, 100 have the condition, producing 90 true positives. The other 9,900 produce 495 false positives. PPV = 90/(90\u002B495) = 15.38%.",
    "calculation": {
      "kind": "ppv",
      "prevalence": 0.01,
      "sensitivity": 0.9,
      "specificity": 0.95,
      "expected": 0.15384615384615385
    },
    "help": "clinical_epidemiology/diagnostic.htm",
    "operation": "MiscDiagnostic"
  },
  {
    "id": "PG-CI-01",
    "version": 2,
    "track": "medicine",
    "topic": "Confidence intervals",
    "objective": "Distinguish inconclusive evidence from evidence of no effect.",
    "stem": "A trial estimates a treatment-to-control risk ratio for an adverse outcome of 0.76 with a 95% confidence interval from 0.58 to 1.03. Which conclusion best describes these results?",
    "options": [
      {
        "id": "A",
        "text": "The trial proves that the intervention has no effect.",
        "feedback": "Including 1 does not prove the absence of an effect."
      },
      {
        "id": "B",
        "text": "The results are compatible with appreciable benefit and a small increase in risk; they do not exclude no effect.",
        "feedback": "The interval spans a 42% reduction through a 3% increase and includes a risk ratio of 1."
      },
      {
        "id": "C",
        "text": "There is a 95% probability that this particular interval contains the true effect.",
        "feedback": "That is not the frequentist repeated-sampling interpretation of a confidence interval."
      },
      {
        "id": "D",
        "text": "The intervention reduces risk by exactly 24% in every patient.",
        "feedback": "A group-level point estimate is uncertain and is not an individual guarantee."
      },
      {
        "id": "E",
        "text": "Increasing the sample size would guarantee a statistically significant benefit.",
        "feedback": "A larger sample can improve precision but cannot guarantee that conclusion."
      }
    ],
    "correct": "B",
    "hint": "For a ratio, what value represents no difference? What range of effects does the interval allow?",
    "lesson": "For risk ratios the null value is 1; for risk differences it is 0. An interval that includes the null can also include important benefit or harm. Its width tells you about precision.",
    "explanation": "The estimate favours benefit, but the interval includes no effect and a small increase in risk. Interpret the magnitude and precision together; clinical decisions also require harms, costs and other evidence.",
    "help": "basics/confidence_interval.htm",
    "operation": "PropUnPaired"
  },
  {
    "id": "PG-RROR-01",
    "version": 2,
    "track": "medicine",
    "topic": "Risk and odds",
    "objective": "Recognise that odds ratios and risk ratios differ for common outcomes.",
    "stem": "In a fictional cohort, an outcome occurs in 120 of 200 exposed people and 80 of 200 unexposed people. Which pair gives the exposed-to-unexposed risk ratio and odds ratio?",
    "options": [
      {
        "id": "A",
        "text": "Risk ratio 1.50; odds ratio 2.25",
        "feedback": "Risks are 0.6 and 0.4; odds are 120/80 and 80/120."
      },
      {
        "id": "B",
        "text": "Risk ratio 2.25; odds ratio 1.50",
        "feedback": "These two statistics have been exchanged."
      },
      {
        "id": "C",
        "text": "Risk ratio 1.50; odds ratio 1.50",
        "feedback": "These outcomes are common: the odds ratio is 2.25, not 1.50. Rarity in both groups is a useful sufficient condition for the usual risk-ratio approximation."
      },
      {
        "id": "D",
        "text": "Risk ratio 0.67; odds ratio 0.44",
        "feedback": "These compare unexposed with exposed, reversing the requested direction."
      },
      {
        "id": "E",
        "text": "Risk ratio 0.20; odds ratio 0.20",
        "feedback": "0.20 is the absolute risk difference."
      }
    ],
    "correct": "A",
    "hint": "Risk divides events by everyone in the group. Odds divides events by non-events.",
    "lesson": "Always identify the reference group. Risks and odds are different quantities; with common outcomes, an odds ratio can be much further from 1 than the corresponding risk ratio.",
    "explanation": "RR = (120/200)/(80/200) = 1.5. OR = (120/80)/(80/120) = 2.25. These observational comparisons do not by themselves establish causation.",
    "calculation": {
      "kind": "riskOdds",
      "a": 120,
      "b": 80,
      "c": 80,
      "d": 120,
      "expected": [
        1.5,
        2.25
      ]
    },
    "help": "clinical_epidemiology/risk_prospective.htm",
    "operation": "MiscRelRisk"
  },
  {
    "id": "PG-SE-01",
    "version": 1,
    "track": "medicine",
    "topic": "Precision and sample size",
    "objective": "Apply the square-root relationship between sample size and standard error.",
    "stem": "For independent observations from the same population, assume the standard deviation stays fixed. If the sample size increases from 100 to 400, what happens to the standard error of the sample mean?",
    "options": [
      {
        "id": "A",
        "text": "It is unchanged.",
        "feedback": "The standard error depends on sample size even when the population standard deviation is unchanged."
      },
      {
        "id": "B",
        "text": "It becomes one quarter as large.",
        "feedback": "Standard error falls with the square root of sample size, not sample size itself."
      },
      {
        "id": "C",
        "text": "It doubles.",
        "feedback": "A larger independent sample makes the mean more precise."
      },
      {
        "id": "D",
        "text": "It becomes half as large.",
        "feedback": "SE = SD/\u221An, so the ratio is \u221A(100/400) = 0.5."
      },
      {
        "id": "E",
        "text": "It becomes zero.",
        "feedback": "Finite samples retain sampling uncertainty."
      }
    ],
    "correct": "D",
    "hint": "Use SE = SD divided by the square root of n.",
    "lesson": "Standard deviation describes variation between observations; standard error describes sampling variation in an estimate. Quadrupling an independent sample halves the standard error when the standard deviation stays fixed.",
    "explanation": "SE(new)/SE(old) = \u221A100/\u221A400 = 0.5. Dependence or clustering changes the calculation, and systematic bias is not removed by increasing sample size.",
    "calculation": {
      "kind": "seRatio",
      "oldN": 100,
      "newN": 400,
      "expected": 0.5
    },
    "help": "basics/precision.htm",
    "operation": "UnivariateSummary"
  },
  {
    "id": "PH-STAND-01",
    "version": 1,
    "track": "publicHealth",
    "topic": "Direct standardisation",
    "objective": "Apply common population weights to age-specific rates.",
    "stem": "A district has annual mortality rates of 200 per 100,000 in its younger age group and 1,000 per 100,000 in its older group. A standard population contains 75% younger and 25% older people. What is the directly age-standardised annual rate per 100,000?",
    "options": [
      {
        "id": "A",
        "text": "200",
        "feedback": "This uses only the younger group."
      },
      {
        "id": "B",
        "text": "300",
        "feedback": "The weighted contribution of the older group alone is 250, in addition to 150 from the younger group."
      },
      {
        "id": "C",
        "text": "400",
        "feedback": "0.75 \u00D7 200 \u002B 0.25 \u00D7 1,000 = 400."
      },
      {
        "id": "D",
        "text": "600",
        "feedback": "This would use equal weights rather than the supplied standard population."
      },
      {
        "id": "E",
        "text": "1,200",
        "feedback": "Adding the rates ignores the population weights."
      }
    ],
    "correct": "C",
    "hint": "Multiply each age-specific rate by its share of the standard population, then add.",
    "lesson": "Direct standardisation applies each population\u2019s stratum-specific rates to a common set of weights. The resulting rate is a comparison summary, not necessarily the population\u2019s observed crude rate.",
    "explanation": "The younger contribution is 150 and the older contribution is 250, giving 400 per 100,000 per year. Use the same standard weights when comparing districts.",
    "calculation": {
      "kind": "weightedRate",
      "rates": [
        200,
        1000
      ],
      "weights": [
        0.75,
        0.25
      ],
      "expected": 400
    },
    "help": "rates/direct_standardization.htm",
    "operation": "RateDirectScreen"
  },
  {
    "id": "PH-CONF-01",
    "version": 2,
    "track": "publicHealth",
    "topic": "Confounding",
    "objective": "Interpret a change between crude and stratified associations.",
    "stem": "An observational comparison gives a crude risk ratio of 2.0. Baseline age precedes exposure, influences exposure choice and predicts the outcome; the exposed group is older. In every age stratum the risk ratio is 1.0, with adequate data and accurate measurement. Which explanation is most consistent with this pattern?",
    "options": [
      {
        "id": "A",
        "text": "The pattern is consistent with confounding by baseline age.",
        "feedback": "Different age distributions can create a crude association when no association remains within age strata."
      },
      {
        "id": "B",
        "text": "Age is certainly a mediator of the exposure effect.",
        "feedback": "Baseline age precedes exposure, so it cannot mediate the effect of that exposure."
      },
      {
        "id": "C",
        "text": "There must be strong effect modification by age.",
        "feedback": "The stated stratum-specific risk ratios do not vary with age; effect modification must be described on a specified scale."
      },
      {
        "id": "D",
        "text": "The exposure has been proved harmless in every setting.",
        "feedback": "Removing this observed association does not establish universal safety or remove other biases."
      },
      {
        "id": "E",
        "text": "The crude risk ratio is necessarily an arithmetic error.",
        "feedback": "A correctly computed crude ratio can differ from stratum-specific ratios."
      }
    ],
    "correct": "A",
    "hint": "Distinguish imbalance in a third variable from variation of the effect across strata.",
    "lesson": "Confounding can mix the effect of an exposure with differences in other determinants of the outcome. Effect modification describes variation across strata on a specified effect scale, such as a risk difference or risk ratio. A causal diagram and design knowledge help decide what to adjust for.",
    "explanation": "Given the stated causal role and imbalance of baseline age, this pattern supports age confounding. A change after adjustment alone cannot diagnose confounding or establish no causal effect; chance, other biases and the causal assumptions still matter.",
    "help": "basics/confounding.htm",
    "operation": "MantelHaenszelScreen"
  },
  {
    "id": "PH-CLUST-01",
    "version": 1,
    "track": "publicHealth",
    "topic": "Clustered observations",
    "objective": "Recognise the loss of precision from within-cluster correlation.",
    "stem": "A survey uses equal clusters of 20 people. For its outcome, the intracluster correlation is 0.04. Using the approximation DE = 1 \u002B (m \u2212 1)\u03C1, what is the design effect relative to simple random sampling?",
    "options": [
      {
        "id": "A",
        "text": "1.04",
        "feedback": "The correlation is multiplied by m\u22121, not added just once."
      },
      {
        "id": "B",
        "text": "1.20",
        "feedback": "Insert m = 20 and \u03C1 = 0.04 into the stated formula."
      },
      {
        "id": "C",
        "text": "1.76",
        "feedback": "1 \u002B 19 \u00D7 0.04 = 1.76."
      },
      {
        "id": "D",
        "text": "2.00",
        "feedback": "The supplied values give a smaller variance inflation."
      },
      {
        "id": "E",
        "text": "20.00",
        "feedback": "A design effect of 20 under this formula would require perfect within-cluster correlation."
      }
    ],
    "correct": "C",
    "hint": "Here m is the number of people in each cluster, not the number of clusters.",
    "lesson": "People in one cluster may provide partly overlapping information. This approximation multiplies the variance by the design effect; standard errors are multiplied by its square root. Unequal cluster sizes and few clusters need more careful planning.",
    "explanation": "DE = 1 \u002B (20\u22121) \u00D7 0.04 = 1.76. Under this approximation, nominal sample size divided by 1.76 gives an effective sample size; it is not a substitute for a full cluster-design calculation.",
    "calculation": {
      "kind": "designEffect",
      "m": 20,
      "rho": 0.04,
      "expected": 1.76
    },
    "help": "basics/precision.htm",
    "operation": null
  },
  {
    "id": "PH-SMR-01",
    "version": 2,
    "track": "publicHealth",
    "topic": "Standardised mortality ratio",
    "objective": "Interpret observed relative to expected events.",
    "stem": "A population has 55 observed deaths over the study period. Applying reference age-specific mortality rates to its age-specific person-time gives 44 expected deaths. What is the standardised mortality ratio, expressed as a ratio rather than a percentage?",
    "options": [
      {
        "id": "A",
        "text": "0.20",
        "feedback": "This is neither the observed-to-expected ratio nor its reciprocal."
      },
      {
        "id": "B",
        "text": "0.80",
        "feedback": "This reverses observed and expected."
      },
      {
        "id": "C",
        "text": "1.25",
        "feedback": "55/44 = 1.25."
      },
      {
        "id": "D",
        "text": "11",
        "feedback": "Eleven is the difference in counts."
      },
      {
        "id": "E",
        "text": "125",
        "feedback": "125 is the SMR expressed on a percentage scale, not the requested ratio scale."
      }
    ],
    "correct": "C",
    "hint": "Use observed deaths divided by expected deaths.",
    "lesson": "An SMR compares observed with expected events based on reference stratum-specific rates. Always state whether you use a ratio scale (null 1) or percentage scale (null 100), and include uncertainty. SMRs for different populations may use different implicit weights, so they are not automatically comparable with each other.",
    "explanation": "SMR = 55/44 = 1.25, or 25% more deaths than expected on this reference. The point estimate alone does not establish statistical significance or causation.",
    "calculation": {
      "kind": "smr",
      "observed": 55,
      "expectedDeaths": 44,
      "expected": 1.25
    },
    "help": "rates/smr.htm",
    "operation": "RateSmrScreen"
  },
  {
    "id": "AN-MED-01",
    "version": 1,
    "track": "analyst",
    "topic": "Skewed service data",
    "objective": "Choose a resistant description of a typical waiting time.",
    "stem": "Most clinic waits are 10\u201325 minutes, but a few exceed two hours. Which summary is least affected by these unusually long waits when describing a typical patient\u2019s wait?",
    "options": [
      {
        "id": "A",
        "text": "Mean and standard deviation",
        "feedback": "Both are influenced by unusually large values."
      },
      {
        "id": "B",
        "text": "Median and interquartile range",
        "feedback": "The median describes the middle observation and the IQR the middle half."
      },
      {
        "id": "C",
        "text": "Maximum only",
        "feedback": "The maximum describes an extreme, not a typical observation."
      },
      {
        "id": "D",
        "text": "Standard error only",
        "feedback": "This concerns precision of an estimate, not a typical wait."
      },
      {
        "id": "E",
        "text": "P value",
        "feedback": "This does not describe the distribution."
      }
    ],
    "correct": "B",
    "hint": "Consider a measure based on ordered positions rather than the sum.",
    "lesson": "Use a histogram with median and IQR for a resistant summary, while retaining the long waits as operationally important observations.",
    "explanation": "Median and IQR resist the influence of extreme waits. They do not make long waits irrelevant: report tail performance separately when it matters.",
    "help": "basic_descriptive_statistics/univariate_summary.htm",
    "operation": "UnivariateSummary"
  },
  {
    "id": "AN-PCT-01",
    "version": 1,
    "track": "analyst",
    "topic": "Percentage points",
    "objective": "Distinguish an absolute percentage-point change from a relative change.",
    "stem": "The non-attendance rate falls from 10% to 8%. Which statement correctly describes both the absolute and relative reduction?",
    "options": [
      {
        "id": "A",
        "text": "2 percentage points; 20% relative reduction",
        "feedback": "(0.10\u22120.08)/0.10 = 0.20."
      },
      {
        "id": "B",
        "text": "2 percentage points; 2% relative reduction",
        "feedback": "The relative reduction divides by the starting risk."
      },
      {
        "id": "C",
        "text": "20 percentage points; 2% relative reduction",
        "feedback": "This reverses and mis-scales the changes."
      },
      {
        "id": "D",
        "text": "8 percentage points; 80% relative reduction",
        "feedback": "These describe the new rate or its ratio, not the reduction."
      },
      {
        "id": "E",
        "text": "No reduction",
        "feedback": "The rate decreased."
      }
    ],
    "correct": "A",
    "hint": "Subtract for the absolute change; divide that change by the starting value for the relative change.",
    "lesson": "State both absolute and relative changes, and include denominators and time periods.",
    "explanation": "The absolute fall is 0.02, or two percentage points. Relative to the original 0.10 it is a 20% reduction. These rates alone do not establish what caused the reduction.",
    "help": "clinical_epidemiology/risk_prospective.htm",
    "operation": "MiscRelRisk"
  },
  {
    "id": "AN-CAUS-01",
    "version": 1,
    "track": "analyst",
    "topic": "Association and causation",
    "objective": "Recognise confounding in service comparisons.",
    "stem": "Hospitals with more specialist staff also have higher mortality. They receive the most severely ill patients. What is the best first interpretation?",
    "options": [
      {
        "id": "A",
        "text": "Specialists cause deaths",
        "feedback": "The observational comparison does not support this causal claim."
      },
      {
        "id": "B",
        "text": "Severity may confound the association",
        "feedback": "Severity can affect staffing needs and mortality."
      },
      {
        "id": "C",
        "text": "More staff must prevent every death",
        "feedback": "This is not supported either."
      },
      {
        "id": "D",
        "text": "Mortality cannot be compared statistically",
        "feedback": "Comparisons are possible, with careful design and adjustment."
      },
      {
        "id": "E",
        "text": "A small P value would eliminate confounding",
        "feedback": "Statistical significance does not remove systematic bias."
      }
    ],
    "correct": "B",
    "hint": "What differs between the patients cared for by these hospitals?",
    "lesson": "Case mix is essential to interpreting service comparisons. Adjustment needs design knowledge and measured relevant variables.",
    "explanation": "Severity is a plausible confounder. Compare like with like where possible and describe remaining limitations; an adjusted model alone does not prove causation.",
    "help": "basics/confounding.htm",
    "operation": null
  },
  {
    "id": "AN-PREC-01",
    "version": 2,
    "track": "analyst",
    "topic": "Precision of a rate",
    "objective": "Relate denominators to uncertainty.",
    "stem": "Two services both observe a 10% event rate. One records 10 events among 100 people, the other 100 among 1,000. Using the same confidence level and binomial interval method, and independent comparable observations, which usually has the narrower interval for the underlying proportion?",
    "options": [
      {
        "id": "A",
        "text": "The service with 100 people",
        "feedback": "It has less information about the population proportion."
      },
      {
        "id": "B",
        "text": "The service with 1,000 people",
        "feedback": "A larger independent denominator improves precision at the same observed rate."
      },
      {
        "id": "C",
        "text": "Both must have identical intervals",
        "feedback": "Equal point estimates can have different uncertainty."
      },
      {
        "id": "D",
        "text": "Neither can have an interval",
        "feedback": "A confidence interval can be calculated for either proportion."
      },
      {
        "id": "E",
        "text": "The service with the smaller P value, regardless of denominator",
        "feedback": "The question concerns precision of an estimated rate."
      }
    ],
    "correct": "B",
    "hint": "Consider the standard error \u221A(p(1\u2212p)/n).",
    "lesson": "Show denominators and uncertainty alongside performance rates.",
    "explanation": "At the same rate, a larger independent sample generally gives a narrower interval. Clustering, bias and different case mix can complicate real comparisons.",
    "help": "basics/confidence_interval.htm",
    "operation": null
  },
  {
    "id": "RS-PAIR-01",
    "version": 1,
    "track": "researcher",
    "topic": "Paired assumptions",
    "objective": "Identify the relevant distributional assumption for a paired t test.",
    "stem": "Which distribution should be checked for major departures from normality when using a paired t test with a small sample of independent pairs?",
    "options": [
      {
        "id": "A",
        "text": "The pooled before and after measurements",
        "feedback": "Pooling ignores pairing."
      },
      {
        "id": "B",
        "text": "The within-pair differences",
        "feedback": "The test is a one-sample t test of these differences."
      },
      {
        "id": "C",
        "text": "The participant identification numbers",
        "feedback": "Identifiers have no relevant distribution."
      },
      {
        "id": "D",
        "text": "The P values",
        "feedback": "These are outputs rather than the observations being modelled."
      },
      {
        "id": "E",
        "text": "Only the before measurements",
        "feedback": "The test operates on differences."
      }
    ],
    "correct": "B",
    "hint": "What single observation enters the test for each pair?",
    "lesson": "Check differences and the independence of pairs; investigate outliers and whether the study design supports the intended inference.",
    "explanation": "The paired t test models within-pair differences. It does not require the two original measurement distributions individually to be normal.",
    "help": "parametric_methods/paired_t.htm",
    "operation": "TPaired"
  },
  {
    "id": "RS-MULT-01",
    "version": 2,
    "track": "researcher",
    "topic": "Multiple testing",
    "objective": "Recognise false-positive risk across many tests.",
    "stem": "Twenty tests are independent under their null hypotheses, which are all true. Each test has an actual false-positive probability of 5%. What is the probability of at least one false positive, approximately?",
    "options": [
      {
        "id": "A",
        "text": "5%",
        "feedback": "This is the level for one test, not the whole family."
      },
      {
        "id": "B",
        "text": "10%",
        "feedback": "Use the probability of no false positives across all tests."
      },
      {
        "id": "C",
        "text": "36%",
        "feedback": "This is approximately the probability of no false positives."
      },
      {
        "id": "D",
        "text": "64%",
        "feedback": "1\u22120.95^20 is approximately 0.642."
      },
      {
        "id": "E",
        "text": "100%",
        "feedback": "A false positive is not guaranteed."
      }
    ],
    "correct": "D",
    "hint": "The chance of no false positives is 0.95 multiplied by itself twenty times.",
    "lesson": "Prespecify analyses and distinguish confirmatory from exploratory work. Multiplicity affects interpretation.",
    "explanation": "Under the stated independence assumption, P(at least one) = 1\u22120.95^20 \u2248 64.2%. Dependence changes this calculation.",
    "help": "basics/p_values.htm",
    "operation": null
  },
  {
    "id": "RS-MISS-01",
    "version": 1,
    "track": "researcher",
    "topic": "Missing data",
    "objective": "Recognise bias from outcome-related missingness.",
    "stem": "Participants with the worst symptoms are more likely to miss their follow-up visit. What is the main concern with analysing only complete records?",
    "options": [
      {
        "id": "A",
        "text": "It necessarily removes all bias",
        "feedback": "Removing missing records does not fix selective follow-up."
      },
      {
        "id": "B",
        "text": "It always gives the same result as full follow-up",
        "feedback": "Missingness can depend on outcomes."
      },
      {
        "id": "C",
        "text": "It may produce a biased estimate as well as lose precision",
        "feedback": "Complete cases may systematically differ from those missing."
      },
      {
        "id": "D",
        "text": "It proves the symptoms improved",
        "feedback": "Non-attendance is not an observed improvement."
      },
      {
        "id": "E",
        "text": "A larger sample guarantees no bias",
        "feedback": "More observations do not remove systematic selection."
      }
    ],
    "correct": "C",
    "hint": "Are the observed participants representative of everyone originally enrolled?",
    "lesson": "Describe missingness, investigate its causes and justify assumptions for sensitivity analyses or imputation.",
    "explanation": "Outcome-related missingness can make complete cases unrepresentative. The direction and size of bias depend on the setting and missingness process.",
    "help": "basics/bias.htm",
    "operation": null
  },
  {
    "id": "RS-CI-01",
    "version": 1,
    "track": "researcher",
    "topic": "Confidence interval interpretation",
    "objective": "Interpret repeated-sampling coverage.",
    "stem": "What does a 95% frequentist confidence procedure aim to achieve under its assumptions?",
    "options": [
      {
        "id": "A",
        "text": "95% of individual observations lie in every interval",
        "feedback": "This confuses a parameter interval with a range of observations."
      },
      {
        "id": "B",
        "text": "95% of intervals from repeated samples contain the fixed population parameter",
        "feedback": "Coverage refers to the procedure across repeated samples."
      },
      {
        "id": "C",
        "text": "The null hypothesis is true with probability 95%",
        "feedback": "This is not a posterior probability."
      },
      {
        "id": "D",
        "text": "95% of future studies give exactly the same estimate",
        "feedback": "Estimates vary between samples."
      },
      {
        "id": "E",
        "text": "Bias is eliminated in 95% of studies",
        "feedback": "Coverage relies on model and design assumptions."
      }
    ],
    "correct": "B",
    "hint": "Imagine repeating the sampling and interval calculation many times.",
    "lesson": "A confidence interval reports precision under assumptions; it does not protect against every source of bias.",
    "explanation": "The 95% refers to long-run coverage of the fixed parameter by the interval procedure, provided the relevant assumptions hold.",
    "help": "basics/confidence_interval.htm",
    "operation": null
  },
  {
    "id": "PG-ROC-01",
    "version": 2,
    "track": "medicine",
    "topic": "Diagnostic thresholds",
    "objective": "Recognise the sensitivity\u2013specificity trade-off.",
    "stem": "In a fictional study, a blood marker is considered positive above a chosen cut-off. Researchers raise that cut-off while keeping the same participants and reference diagnoses. Which change in classification is possible?",
    "options": [
      {
        "id": "A",
        "text": "Some previously negative results become positive",
        "feedback": "Raising the cut-off cannot do this on the same data."
      },
      {
        "id": "B",
        "text": "Some true positives and false positives become negative",
        "feedback": "A higher cut-off can reduce both true-positive and false-positive counts."
      },
      {
        "id": "C",
        "text": "Sensitivity must increase",
        "feedback": "A higher cut-off cannot capture additional positive cases on the same data."
      },
      {
        "id": "D",
        "text": "Specificity must decrease",
        "feedback": "With the same disease-negative denominator, fewer false positives mean higher specificity; with none changing, specificity is unchanged."
      },
      {
        "id": "E",
        "text": "Both denominators change automatically",
        "feedback": "Disease status is fixed by the reference diagnoses in this study."
      }
    ],
    "correct": "B",
    "hint": "Think about people whose marker lies between the old and new cut-offs.",
    "lesson": "Choosing a threshold changes the balance of missed cases and false alarms. Inspect sensitivity and specificity together; predictive values also depend on the setting.",
    "explanation": "Values between the two cut-offs change from positive to negative. This can lower sensitivity and raise specificity; if nobody falls between the cut-offs the classifications stay unchanged.",
    "help": "clinical_epidemiology/diagnostic.htm",
    "operation": "MiscDiagnostic",
    "inspiration": "Public MRCP-style diagnostic threshold examples; original scenario and options."
  },
  {
    "id": "PG-LR-01",
    "version": 1,
    "track": "medicine",
    "topic": "Pre-test and post-test probability",
    "objective": "Update odds with a likelihood ratio.",
    "stem": "For a fictional test, the positive likelihood ratio is 6. Before testing, the probability of the condition is 10%. What is the approximate probability after a positive result, assuming the likelihood ratio applies in this setting?",
    "options": [
      {
        "id": "A",
        "text": "6%",
        "feedback": "The likelihood ratio is a multiplier of odds, not a probability."
      },
      {
        "id": "B",
        "text": "16%",
        "feedback": "Adding a likelihood ratio to a probability is not valid."
      },
      {
        "id": "C",
        "text": "40%",
        "feedback": "Prior odds 1/9 become 6/9; probability is (6/9)/(1\u002B6/9) = 0.4."
      },
      {
        "id": "D",
        "text": "60%",
        "feedback": "Multiply odds, rather than multiplying the probability by 6."
      },
      {
        "id": "E",
        "text": "100%",
        "feedback": "A positive test does not guarantee the condition."
      }
    ],
    "correct": "C",
    "hint": "First convert the 10% probability to odds p/(1\u2212p).",
    "lesson": "Likelihood ratios update odds. Convert back to a probability after multiplication.",
    "explanation": "Pre-test odds = 0.1/0.9 = 1/9. Post-test odds = 6/9 = 2/3. Post-test probability = (2/3)/(1\u002B2/3) = 0.4, or 40%.",
    "help": "clinical_epidemiology/diagnostic.htm",
    "operation": "MiscDiagnostic",
    "inspiration": "Public MRCP-style diagnostic evidence teaching; independently constructed numerical question."
  },
  {
    "id": "PG-FOREST-01",
    "version": 2,
    "track": "medicine",
    "topic": "Interpreting effect estimates",
    "objective": "Distinguish effect size from statistical evidence.",
    "stem": "Two fictional studies estimate treatment-to-control risk ratios for the same adverse outcome. Study A: 0.40 (95% CI 0.10\u20131.60). Study B: 0.85 (95% CI 0.80\u20130.91). Assuming valid corresponding two-sided tests, which interpretation is best?",
    "options": [
      {
        "id": "A",
        "text": "Study A has stronger evidence solely because its ratio is further from 1",
        "feedback": "Effect magnitude alone does not measure statistical evidence."
      },
      {
        "id": "B",
        "text": "Only Study B rejects the null risk ratio of 1 at the 5% level",
        "feedback": "Study B\u2019s interval excludes 1; Study A\u2019s includes it."
      },
      {
        "id": "C",
        "text": "Study A proves there is no benefit",
        "feedback": "Its wide interval includes both substantial benefit and harm."
      },
      {
        "id": "D",
        "text": "Study B necessarily has the more important clinical benefit",
        "feedback": "Clinical importance needs context and absolute risks."
      },
      {
        "id": "E",
        "text": "Both intervals exclude no effect",
        "feedback": "Study A includes the null ratio of 1."
      }
    ],
    "correct": "B",
    "hint": "Inspect whether each confidence interval includes the null ratio of 1.",
    "lesson": "Read the point estimate and interval together. A large but imprecise estimate does not automatically provide stronger evidence than a smaller, precise one.",
    "explanation": "Only Study B\u2019s interval excludes 1. This does not settle clinical importance, bias, comparability or a direct difference between the two studies.",
    "help": "basics/confidence_interval.htm",
    "operation": null,
    "inspiration": "RCGP November 2025 data-interpretation samples, especially Q41; new example separating magnitude from evidence."
  },
  {
    "id": "AN-RATE-01",
    "version": 1,
    "track": "analyst",
    "topic": "Rates versus counts",
    "objective": "Avoid inferring absolute totals from a rate alone.",
    "stem": "A chart shows Clinic A with 24 referrals per 1,000 registered patients and Clinic B with 18 per 1,000 during the same quarter. Their registered populations are not supplied. Which conclusion is justified by these values alone?",
    "options": [
      {
        "id": "A",
        "text": "Clinic A made more referrals in total",
        "feedback": "The population denominators are unknown."
      },
      {
        "id": "B",
        "text": "Clinic B has fewer registered patients",
        "feedback": "The rates do not reveal population sizes."
      },
      {
        "id": "C",
        "text": "Clinic A has the higher referral rate",
        "feedback": "24 per 1,000 exceeds 18 per 1,000."
      },
      {
        "id": "D",
        "text": "Clinic A\u2019s referrals were less appropriate",
        "feedback": "No appropriateness data are supplied."
      },
      {
        "id": "E",
        "text": "Clinic B provides better care",
        "feedback": "A rate alone does not establish quality."
      }
    ],
    "correct": "C",
    "hint": "What does \u201Cper 1,000\u201D tell you, and what does it leave unknown?",
    "lesson": "Read the axis units and distinguish counts from rates. Rates support comparison but do not by themselves explain quality or total workload.",
    "explanation": "Only the rate comparison follows. Total referrals also depend on population size; appropriateness and quality require additional evidence.",
    "help": "rates/incidence_and_prevalence.htm",
    "operation": null,
    "inspiration": "RCGP November 2025 Q3 and Q20: interpretation of plotted rates; original referral scenario."
  },
  {
    "id": "PH-CASEMIX-01",
    "version": 2,
    "track": "publicHealth",
    "topic": "Case mix and service comparisons",
    "objective": "Identify what is needed before attributing a difference to performance.",
    "stem": "A rehabilitation unit has a higher average length of stay than another unit. It also receives more people with complex disability and has longer waits for supported housing. Which is the most defensible next step?",
    "options": [
      {
        "id": "A",
        "text": "Conclude that its clinical team is inefficient",
        "feedback": "The crude average mixes performance with patient needs and discharge circumstances."
      },
      {
        "id": "B",
        "text": "Delete every stay longer than the median",
        "feedback": "Arbitrary exclusion loses relevant information and may bias the comparison."
      },
      {
        "id": "C",
        "text": "Examine the distribution, case mix and discharge pathways before judging performance",
        "feedback": "All can explain differences in the crude mean."
      },
      {
        "id": "D",
        "text": "Compare only the two largest stays",
        "feedback": "Extremes alone cannot characterise the services."
      },
      {
        "id": "E",
        "text": "Ignore all service comparisons",
        "feedback": "Careful comparisons can be useful."
      }
    ],
    "correct": "C",
    "hint": "What factors beyond treatment quality influence how long someone stays?",
    "lesson": "Combine descriptive statistics with service context. Investigate skew, recording conventions, patient needs and discharge support.",
    "explanation": "Distribution, case mix and the wider care pathway matter. Adjusted comparisons still require appropriate data and assumptions; a crude mean alone cannot attribute causation. Define whether discharge support is part of the service effect being evaluated before deciding to adjust for it.",
    "help": "basic_descriptive_statistics/univariate_summary.htm",
    "operation": "UnivariateSummary",
    "inspiration": "FPH DFPH Specimen Paper B Q1: interpretation and case mix; new setting and single-best-answer format."
  },
  {
    "id": "RS-LEAD-01",
    "version": 1,
    "track": "researcher",
    "topic": "Screening and lead-time bias",
    "objective": "Distinguish earlier diagnosis from longer life.",
    "stem": "A fictional screening programme diagnoses a disease two years earlier, but people die at the same age they would have without screening. Measured survival from diagnosis rises. What explains this rise?",
    "options": [
      {
        "id": "A",
        "text": "A demonstrated reduction in mortality",
        "feedback": "Death timing has not changed."
      },
      {
        "id": "B",
        "text": "Lead-time bias",
        "feedback": "Earlier diagnosis starts the survival clock sooner without postponing death."
      },
      {
        "id": "C",
        "text": "Randomisation",
        "feedback": "The scenario does not describe random allocation."
      },
      {
        "id": "D",
        "text": "A guaranteed treatment benefit",
        "feedback": "Longer measured survival alone does not establish benefit."
      },
      {
        "id": "E",
        "text": "A fall in disease incidence",
        "feedback": "No such change is stated."
      }
    ],
    "correct": "B",
    "hint": "Which event starts the survival clock?",
    "lesson": "Screening evaluation needs outcomes that distinguish earlier detection from improved health. Consider harms and appropriate comparisons.",
    "explanation": "Lead-time bias extends observed time from diagnosis to death when diagnosis happens earlier but death does not. Mortality and other patient-relevant outcomes are needed to assess benefit.",
    "help": "basics/bias.htm",
    "operation": null,
    "inspiration": "FPH critical-appraisal and screening specimens: original bias question."
  },
  {
    "id": "CORE-DESIGN-01",
    "version": 2,
    "track": "core",
    "topic": "Study design and denominators",
    "objective": "Recognise outcome-based sampling in a case-control study.",
    "stem": "Researchers select 120 people with a disease and 240 without it, then compare previous exposure. Which statement best describes what can be estimated directly from this sampling design?",
    "options": [
      {
        "id": "A",
        "text": "The population incidence, using 120/360",
        "feedback": "The numbers with and without disease were selected by design."
      },
      {
        "id": "B",
        "text": "An exposure odds ratio, with suitable control selection and assumptions",
        "feedback": "Case-control sampling can estimate an odds ratio; it does not directly give population risk from these group sizes."
      },
      {
        "id": "C",
        "text": "The population prevalence, using 120/360",
        "feedback": "The investigator fixed the case-control ratio."
      },
      {
        "id": "D",
        "text": "The causal risk difference with no further assumptions",
        "feedback": "Causal interpretation needs design and confounding assumptions."
      },
      {
        "id": "E",
        "text": "The number needed to treat from case counts alone",
        "feedback": "Absolute treatment risks are not supplied."
      }
    ],
    "correct": "B",
    "hint": "Were participants sampled before or after their outcome status was known?",
    "lesson": "Study design determines which denominators and measures have meaning. Recognise case-control, cohort and experimental designs before choosing a statistic.",
    "explanation": "The investigator chooses cases and controls, so their proportion in the sample is not population incidence or prevalence. The meaning of the odds ratio depends on control sampling. Sampling controls from the risk sets can estimate an incidence rate ratio without a rare-disease assumption. Sampling non-cases at the end of a fixed period instead targets an odds ratio for cumulative risk. Causal interpretation needs additional assumptions.",
    "help": "clinical_epidemiology/risk_retrospective.htm",
    "operation": "MiscRetroRisk"
  },
  {
    "id": "CORE-RATE-01",
    "version": 2,
    "track": "core",
    "topic": "Incidence rate and person-time",
    "objective": "Distinguish an incidence rate from a cumulative risk.",
    "stem": "A fictional cohort contributes 600 person-years while participants are at risk, with 12 new events. What is the incidence rate?",
    "options": [
      {
        "id": "A",
        "text": "2 events per 100 person-years",
        "feedback": "12/600 = 0.02 events per person-year."
      },
      {
        "id": "B",
        "text": "2% of participants, regardless of follow-up",
        "feedback": "Person-time is not a count of participants."
      },
      {
        "id": "C",
        "text": "12% per participant",
        "feedback": "The participant denominator is not given."
      },
      {
        "id": "D",
        "text": "50 events per person-year",
        "feedback": "This inverts the rate."
      },
      {
        "id": "E",
        "text": "600 events per year",
        "feedback": "600 is follow-up time, not event count."
      }
    ],
    "correct": "A",
    "hint": "Divide events by total time at risk, then state the units.",
    "lesson": "An incidence rate uses person-time at risk. A risk uses people initially at risk over a specified period, with appropriate treatment of incomplete follow-up.",
    "explanation": "12/600 = 0.02 events per person-year, or 2 per 100 person-years. This cannot automatically be read as a two-percent one-year risk.",
    "help": "rates/incidence_and_prevalence.htm",
    "operation": null
  },
  {
    "id": "CORE-MEDIATOR-01",
    "version": 2,
    "track": "core",
    "topic": "Confounders and mediators",
    "objective": "Match adjustment to the causal effect of interest.",
    "stem": "A randomised exercise intervention can affect blood pressure partly by changing body weight. To estimate the total intervention effect on blood pressure, why might routinely adjusting for weight measured after the intervention be inappropriate?",
    "options": [
      {
        "id": "A",
        "text": "Post-intervention weight is necessarily a baseline confounder",
        "feedback": "It is measured after treatment and may be affected by it."
      },
      {
        "id": "B",
        "text": "It can block part of the pathway through which the intervention acts",
        "feedback": "That pathway contributes to the total effect."
      },
      {
        "id": "C",
        "text": "Adjustment always increases bias in every analysis",
        "feedback": "Appropriate adjustment depends on the target effect and causal structure."
      },
      {
        "id": "D",
        "text": "Randomisation proves that weight is irrelevant",
        "feedback": "Weight may still mediate treatment effects."
      },
      {
        "id": "E",
        "text": "A smaller P value decides which variables to adjust for",
        "feedback": "Variable choice should follow the causal question and assumptions."
      }
    ],
    "correct": "B",
    "hint": "Distinguish a variable that precedes treatment from one affected by treatment.",
    "lesson": "For a total effect, mediators are part of the causal pathway. Direct effects ask a different question and require additional identification assumptions; adding a mediator to a regression does not automatically estimate a causal direct effect.",
    "explanation": "Adjusting for a mediator can remove part of the total effect and may introduce other bias. Define the estimand and causal structure before deciding what to adjust for.",
    "help": "basics/confounding.htm",
    "operation": null
  },
  {
    "id": "CORE-SELECTION-01",
    "version": 2,
    "track": "core",
    "topic": "Selection and collider bias",
    "objective": "Recognise bias caused by conditioning on a common consequence.",
    "stem": "In a fictional population, exposure E and illness Y both influence whether someone attends a specialist clinic. A study includes only clinic attendees. Even if E and Y were otherwise unrelated, what problem could this selection create?",
    "options": [
      {
        "id": "A",
        "text": "It guarantees a representative population sample",
        "feedback": "Attendance selects people through both variables."
      },
      {
        "id": "B",
        "text": "It may create an association by conditioning on a common consequence",
        "feedback": "Attendance can act as a collider between E and Y."
      },
      {
        "id": "C",
        "text": "It eliminates confounding automatically",
        "feedback": "Selection does not guarantee comparability."
      },
      {
        "id": "D",
        "text": "It proves E causes Y",
        "feedback": "The selection process can create a non-causal association."
      },
      {
        "id": "E",
        "text": "It can only reduce random error",
        "feedback": "It can introduce systematic bias."
      }
    ],
    "correct": "B",
    "hint": "Consider the arrows E \u2192 attendance \u2190 Y.",
    "lesson": "Conditioning on a common consequence can open a non-causal association. Eligibility, recruitment, follow-up and analysis restrictions can all matter.",
    "explanation": "Selecting on clinic attendance conditions on a potential collider and can create an association between its causes. The direction and magnitude depend on the data-generating process.",
    "help": "basics/bias.htm",
    "operation": null
  },
  {
    "id": "LIB-RATES-01",
    "version": 1,
    "track": "publicHealth",
    "topic": "Person-time and age standardisation",
    "objective": "Explain a crude rate difference using age-specific rates and a common standard population.",
    "stem": "Two fictional areas have rates of 200 and 1000 events per 100000 person-years in younger and older groups respectively. Both are directly standardised using 75% younger and 25% older weights. Which conclusion is justified?",
    "options": [
      {
        "id": "A",
        "text": "Both adjusted rates are 400 per 100000 person-years.",
        "feedback": "Use 0.75\u00D7200 \u002B 0.25\u00D71000 for each area."
      },
      {
        "id": "B",
        "text": "Both adjusted rates are 600 because the two rates are averaged equally.",
        "feedback": "The stated weights are 75% and 25%, not equal."
      },
      {
        "id": "C",
        "text": "The area with more older people must have a larger adjusted rate.",
        "feedback": "That affects its crude rate; the shared standard weights remove this age-composition difference."
      },
      {
        "id": "D",
        "text": "Both areas have an annual disease risk of exactly 0.4%.",
        "feedback": "A person-time rate is not automatically a one-year cumulative risk."
      },
      {
        "id": "E",
        "text": "Age adjustment proves that the two populations are causally identical.",
        "feedback": "Other differences, biases and uncertainty remain."
      }
    ],
    "correct": "A",
    "hint": "Apply the same stated weights to the age-specific rates in each area.",
    "lesson": "A rate divides events by time at risk. Different age structures can produce different crude rates even when age-specific rates are identical. Direct standardisation applies the same reference weights to each population. A standardised rate is a comparison measure; report its reference population, uncertainty and age-specific pattern.",
    "explanation": "The common weighted rate is 150 \u002B 250 = 400 per 100000 person-years. Different actual age mixes can still produce different crude rates. Equal standardised point estimates do not prove equivalence.",
    "help": "rates/direct_standardization.htm",
    "operation": "RateDirect",
    "lessonId": "rates",
    "sourceIds": [
      "ohid-rates",
      "ohid-dsr",
      "ohid-isr"
    ]
  },
  {
    "id": "LIB-APPRAISAL-01",
    "version": 1,
    "track": "medicine",
    "topic": "Design, missing outcomes and applicability",
    "objective": "Explain what a numerical trial result can and cannot establish.",
    "stem": "In the fictional trial, 20/80 observed programme participants and 30/100 controls have an adverse outcome. Twenty other programme participants have unknown outcomes. Which statement is most defensible?",
    "options": [
      {
        "id": "A",
        "text": "Treat all missing outcomes as non-events because allocation was randomised.",
        "feedback": "Randomisation does not reveal unmeasured outcomes."
      },
      {
        "id": "B",
        "text": "Use 20/80 and ignore the original allocation totals.",
        "feedback": "That describes responders and can give a biased treatment comparison."
      },
      {
        "id": "C",
        "text": "Programme risk among all 100 allocated participants could be 20% to 40% before further assumptions.",
        "feedback": "There are 20 known events and up to 20 additional unknown events."
      },
      {
        "id": "D",
        "text": "The programme definitely reduces risk by 5 percentage points.",
        "feedback": "That observed-data difference does not settle missing-outcome or sampling uncertainty."
      },
      {
        "id": "E",
        "text": "The 20%\u201340% range is a 95% confidence interval.",
        "feedback": "It is a logical bound, not a sampling interval."
      }
    ],
    "correct": "C",
    "hint": "Count how many events there could be if none, or all, of the missing participants had the outcome.",
    "lesson": "Read the research question, allocation, follow-up and outcome measurement before the P value. Risk of bias concerns a particular result; reporting completeness and a large sample do not guarantee validity. Missing outcomes can break the comparability intended by randomisation. Explain effect size, uncertainty and applicability separately.",
    "explanation": "Full-group programme risk ranges from 20/100 to 40/100. Against control risk 30%, this permits benefit or harm. Reasons for missingness, plausible sensitivity analyses and sampling uncertainty all need consideration.",
    "help": "basics/bias.htm",
    "operation": "MiscRelRisk",
    "lessonId": "appraisal",
    "sourceIds": [
      "cochrane-8",
      "rcgp-akt",
      "catalogue-bias"
    ]
  },
  {
    "id": "LIB-MISSING-01",
    "version": 1,
    "track": "researcher",
    "topic": "Patterns, assumptions and sensitivity analysis",
    "objective": "Describe missingness and avoid treating a filled-in value as observed information.",
    "stem": "A researcher replaces every missing symptom score with the mean of the observed scores, then analyses the filled values as if all were measured. What is the main problem?",
    "options": [
      {
        "id": "A",
        "text": "The sample mean must necessarily change.",
        "feedback": "Mean filling preserves the observed mean in this simple setting."
      },
      {
        "id": "B",
        "text": "The missing values have become independently observed measurements.",
        "feedback": "No new measurements have been obtained."
      },
      {
        "id": "C",
        "text": "The method proves that the data are missing completely at random.",
        "feedback": "Imputation does not establish a missingness mechanism."
      },
      {
        "id": "D",
        "text": "It ignores uncertainty about missing values and distorts variability and relationships.",
        "feedback": "Ordinary analysis of the filled values does not account for uncertainty in their imputation."
      },
      {
        "id": "E",
        "text": "It is always identical to properly pooled multiple imputation.",
        "feedback": "Multiple imputation represents missing-value uncertainty across draws and needs valid models and pooling."
      }
    ],
    "correct": "D",
    "hint": "What information was actually added when the same mean was inserted repeatedly?",
    "lesson": "Document the amount, pattern and reasons for missingness before choosing an analysis. MCAR, MAR and MNAR describe assumptions about the missingness process. MAR allows dependence on observed information; it is not a synonym for a random-looking pattern. Replacing missing values with a mean does not restore information or valid uncertainty.",
    "explanation": "Mean filling does not recover the unobserved values. Treating these invented values as observed commonly understates uncertainty and distorts distributions and associations. Investigate missingness and choose a method appropriate to the estimand and assumptions.",
    "help": "basics/missing_data.htm",
    "operation": "UnivariateSummary",
    "lessonId": "missing",
    "sourceIds": [
      "vanbuuren-mechanisms",
      "vanbuuren-simple"
    ]
  },
  {
    "id": "LIB-VALIDATION-01",
    "version": 1,
    "track": "analyst",
    "topic": "Calibration, prediction error and validation",
    "objective": "Assess predictions against outcomes not used to develop the model.",
    "stem": "Eight fictional fixed predictions have errors averaging zero. Ten points are added to every prediction, without changing the observed outcomes. Which statement must be true?",
    "options": [
      {
        "id": "A",
        "text": "Correlation with observed outcomes falls by ten.",
        "feedback": "Correlation is invariant to adding a constant."
      },
      {
        "id": "B",
        "text": "Correlation is unchanged, but mean prediction error becomes \u002B10 points.",
        "feedback": "Adding ten to every prediction adds ten to every prediction-minus-outcome error."
      },
      {
        "id": "C",
        "text": "RMSE is unchanged because the ordering is unchanged.",
        "feedback": "RMSE depends on error size, not just ordering."
      },
      {
        "id": "D",
        "text": "The new predictions are externally validated because correlation is high.",
        "feedback": "A metric alone cannot certify a validation design or usefulness."
      },
      {
        "id": "E",
        "text": "Every observed outcome also increases by ten.",
        "feedback": "Only predictions were changed."
      }
    ],
    "correct": "B",
    "hint": "Separate ranking from the numerical agreement between prediction and outcome.",
    "lesson": "A model can rank outcomes well yet predict systematically wrong values. Evaluate calibration and prediction error as well as association. Performance on the development data is usually optimistic. Internal validation must repeat the modelling process; evaluation in a new relevant setting addresses transportability. A tiny illustration cannot establish clinical usefulness.",
    "explanation": "Correlation is unchanged by an additive constant. With mean original error zero, shifting predictions by \u002B10 gives mean error \u002B10 and increases mean squared error by 100. Calibration and absolute prediction accuracy matter alongside association.",
    "help": "regression_and_correlation/simple_linear.htm",
    "operation": "SimpleLinearRegression",
    "lessonId": "validation",
    "sourceIds": [
      "harrell-reg",
      "tripod-ai"
    ]
  },
  {
    "id": "LIB-META-01",
    "version": 1,
    "track": "researcher",
    "topic": "Meta-analysis, heterogeneity and interpretation",
    "objective": "Explain inverse-variance weighting and decide whether a pooled effect answers a coherent question.",
    "stem": "In a fixed-effect inverse-variance meta-analysis, Study A has SE 0.02 and Study B has SE 0.04 for estimates on the same scale. What is the ratio of A\u2019s weight to B\u2019s weight?",
    "options": [
      {
        "id": "A",
        "text": "1:4",
        "feedback": "That reverses the inverse-variance weighting."
      },
      {
        "id": "B",
        "text": "1:2",
        "feedback": "The smaller SE receives more, not less, weight."
      },
      {
        "id": "C",
        "text": "1:1",
        "feedback": "Equal weighting ignores their different precisions."
      },
      {
        "id": "D",
        "text": "2:1",
        "feedback": "Weights use squared SE, not SE itself."
      },
      {
        "id": "E",
        "text": "4:1",
        "feedback": "(1/0.02\u00B2)/(1/0.04\u00B2) = 4."
      }
    ],
    "correct": "E",
    "hint": "Variance is the square of the standard error; then take its reciprocal.",
    "lesson": "Pooling requires comparable populations, contrasts, outcomes and follow-up, together with an assessment of bias and dependence. Inverse-variance weights favour more precise estimates. A fixed-effect illustration assumes a common underlying effect. Random-effects models allow between-study variation but cannot repair biased studies or justify combining incompatible questions.",
    "explanation": "The weights are 2500 and 625, so A receives four times B\u2019s weight. This does not make A four times as free from bias; inverse-variance weighting represents statistical precision under the model.",
    "help": "meta_analysis/summary.htm",
    "operation": "MetaSummary",
    "lessonId": "meta",
    "sourceIds": [
      "cochrane-10",
      "cochrane-8"
    ]
  },
  {
    "id": "BIO-DATA-01",
    "version": 1,
    "track": "foundation",
    "topic": "Skewed continuous data",
    "objective": "Choose a primary descriptive summary.",
    "stem": "A clinic wants to describe typical appointment waits. Most recorded waits are between 5 and 20 minutes, with a long tail up to 95 minutes. Which pair of summaries best describes the centre and middle spread?",
    "options": [
      {
        "id": "A",
        "text": "Mean and range",
        "feedback": "Both can be strongly influenced by the longest wait."
      },
      {
        "id": "B",
        "text": "Median and interquartile range",
        "feedback": "These summarise the centre and middle half without being strongly pulled by the long tail."
      },
      {
        "id": "C",
        "text": "Mode and standard error of the mean",
        "feedback": "The standard error measures precision of a mean, not spread across patients."
      },
      {
        "id": "D",
        "text": "Standard deviation and sample size",
        "feedback": "Neither gives the requested location."
      },
      {
        "id": "E",
        "text": "Maximum and interquartile range",
        "feedback": "The maximum describes an extreme, not the centre."
      }
    ],
    "correct": "B",
    "hint": "Match the measure to the feature being described: a typical value and middle spread.",
    "lesson": "Tail shape helps determine which centre and spread are most interpretable.",
    "explanation": "Use the median and IQR for the stated aim. Include a display, range or upper percentiles if long waits matter.",
    "lessonId": "describe-data",
    "sourceIds": [
      "gmc-graduates",
      "dfph-syllabus"
    ],
    "help": "basic_descriptive_statistics/univariate_summary.htm",
    "difficulty": "beginner",
    "learnerGoals": [
      "undergraduateMedicine",
      "healthcareBusinessAnalyst"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "451bf9f05bc2fda3f41fc32a14269968eac15a530b0b3b3edd821e8614cb5cd2"
    }
  },
  {
    "id": "BIO-DATA-02",
    "version": 1,
    "track": "foundation",
    "topic": "Quantitative displays",
    "objective": "Choose a chart that shows a distribution.",
    "stem": "A team has one systolic blood pressure measurement in mmHg for each of 240 adults. They want to see the shape of the distribution while retaining absolute measurement units. Which display is most appropriate?",
    "options": [
      {
        "id": "A",
        "text": "A pie chart using one slice per adult",
        "feedback": "The observations are measurements, not shares of a whole."
      },
      {
        "id": "B",
        "text": "A line joining records in patient-ID order",
        "feedback": "Arbitrary record order can suggest a trend with no scientific meaning."
      },
      {
        "id": "C",
        "text": "A scatterplot of blood pressure against itself",
        "feedback": "This produces an identity line and obscures the marginal frequencies."
      },
      {
        "id": "D",
        "text": "A histogram of blood pressure with equal-width bins",
        "feedback": "The horizontal axis retains mmHg and bin heights show how observations are distributed."
      },
      {
        "id": "E",
        "text": "A bar chart of the mean blood pressure only",
        "feedback": "One mean cannot show spread, shape or tails."
      }
    ],
    "correct": "D",
    "hint": "Ask which display keeps measurement units and shows frequencies across a numeric range.",
    "lesson": "Histograms display frequencies across quantitative intervals.",
    "explanation": "A labelled histogram groups blood pressure into bins and shows spread, skew and possible multiple peaks.",
    "lessonId": "describe-data",
    "sourceIds": [
      "gmc-mla",
      "dfph-syllabus"
    ],
    "help": "basic_descriptive_statistics/univariate_summary.htm",
    "difficulty": "beginner",
    "learnerGoals": [
      "undergraduateMedicine",
      "publicHealth"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "e3dd3027bb1ab015bf1dd759fa75a2e0af2e4d363a98964128cff6340df363a3"
    }
  },
  {
    "id": "BIO-DATA-03",
    "version": 2,
    "track": "medicine",
    "topic": "Probability models",
    "objective": "Recognise the binomial setting.",
    "stem": "In a fictional reliability study, the cancellation outcomes for 20 appointments are independent. Each appointment has a 0.15 chance of being cancelled. Which model describes the number cancelled?",
    "options": [
      {
        "id": "A",
        "text": "Binomial with n = 20 and p = 0.15",
        "feedback": "The count is the sum of a fixed number of independent Bernoulli outcomes with common probability."
      },
      {
        "id": "B",
        "text": "Bernoulli with p = 0.15",
        "feedback": "This would describe one appointment only."
      },
      {
        "id": "C",
        "text": "Normal with mean 0.15",
        "feedback": "The count is discrete and its mean is 20 times 0.15."
      },
      {
        "id": "D",
        "text": "Poisson with mean 0.15",
        "feedback": "This proposed mean is incorrect; the exact count model under the stated design is binomial."
      },
      {
        "id": "E",
        "text": "Uniform on integers 0 to 20",
        "feedback": "The possible counts are not equally likely."
      }
    ],
    "correct": "A",
    "hint": "Identify what is counted, how many opportunities occur and the probability of each.",
    "lesson": "A binomial count requires a fixed number of independent trials and a common success probability.",
    "explanation": "Under the stated assumptions, the exact model is Binomial(20, 0.15), with expected count 3.",
    "lessonId": "describe-data",
    "sourceIds": [
      "dfph-syllabus"
    ],
    "help": "basic_descriptive_statistics/univariate_summary.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "undergraduateMedicine",
      "postgraduateMedicine",
      "publicHealth"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "201335490035a2ba30828d67deb215af7dcb81e4357b7724473b9fdf099dc458"
    }
  },
  {
    "id": "BIO-METHOD-01",
    "version": 1,
    "track": "medicine",
    "topic": "Independent and paired designs",
    "objective": "Select a mean comparison from the design.",
    "stem": "A clinic samples 25 patients in June and 25 different, independently sampled patients in July. The outcome is continuous, roughly symmetric in each month and substantially more variable in July. Which method targets the difference in mean outcome while allowing unequal variances?",
    "options": [
      {
        "id": "A",
        "text": "Paired t test",
        "feedback": "There are no linked pairs; putting patients in adjacent rows does not create a match."
      },
      {
        "id": "B",
        "text": "Mann\u2013Whitney test",
        "feedback": "A rank comparison does not directly estimate the difference in means."
      },
      {
        "id": "C",
        "text": "Welch two-sample t test",
        "feedback": "It compares independent means and allows different group variances."
      },
      {
        "id": "D",
        "text": "Pooled two-sample t test",
        "feedback": "Its pooled standard error assumes equal population variances."
      },
      {
        "id": "E",
        "text": "McNemar test",
        "feedback": "This is for paired binary responses."
      }
    ],
    "correct": "C",
    "hint": "Identify the unit, relationship between groups and target effect before looking at the method names.",
    "lesson": "Design determines independence; the target here is a mean difference.",
    "explanation": "Welch\u0027s t method estimates the difference in means with separate group variances and a degrees-of-freedom approximation.",
    "lessonId": "choose-analysis",
    "sourceIds": [
      "gmc-graduates",
      "mrcp-part1",
      "dfph-syllabus"
    ],
    "help": "parametric_methods/unpaired_t.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "undergraduateMedicine",
      "postgraduateMedicine",
      "publicHealth"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "4aebde7d85b881a2deb29858e0705d5ab257dbe843d4a5a34fec0647c12a6627"
    }
  },
  {
    "id": "BIO-METHOD-02",
    "version": 1,
    "track": "medicine",
    "topic": "Sparse contingency tables",
    "objective": "Choose a test for an independent sparse 2\u00D72 table.",
    "stem": "In an independent randomised pilot, 1 of 10 participants receiving a new process and 5 of 10 controls have a prespecified binary event. A two-sided test of association is wanted. Which method provides an exact conditional test?",
    "options": [
      {
        "id": "A",
        "text": "Pearson chi-square without continuity correction",
        "feedback": "This uses an asymptotic reference distribution, which can be unreliable with sparse expected counts."
      },
      {
        "id": "B",
        "text": "McNemar exact test",
        "feedback": "This requires paired binary observations."
      },
      {
        "id": "C",
        "text": "Ordinary Pearson correlation with a normal-theory P value",
        "feedback": "This is not the requested exact conditional table test."
      },
      {
        "id": "D",
        "text": "Welch t test",
        "feedback": "It is not the intended exact conditional test on the 2\u00D72 counts."
      },
      {
        "id": "E",
        "text": "Fisher exact test",
        "feedback": "It calculates probabilities over the tables compatible with the margins under the null."
      }
    ],
    "correct": "E",
    "hint": "Work out how many events would be expected in each group under no association.",
    "lesson": "Sparse tables may call for exact rather than asymptotic inference.",
    "explanation": "Each group has three expected events under the null. Fisher\u0027s exact test meets the request for a conditional exact test.",
    "lessonId": "choose-analysis",
    "sourceIds": [
      "mrcp-part1",
      "dfph-syllabus"
    ],
    "help": "exact_tests_on_counts/fisher_exact.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "74fadb48f17ca45af96e37625e9e48dcacd917bf294ad4740a6a0ffdea74a7ad"
    }
  },
  {
    "id": "BIO-METHOD-03",
    "version": 1,
    "track": "publicHealth",
    "topic": "Rank correlation",
    "objective": "Identify an association measure for an ordered relationship.",
    "stem": "A scatterplot of two variables measured once per participant shows a steadily increasing curved relationship with no reversal. Which statistic best summarises the strength of the monotonic association using ranks?",
    "options": [
      {
        "id": "A",
        "text": "Pearson correlation",
        "feedback": "It summarises linear association using the original values."
      },
      {
        "id": "B",
        "text": "Spearman rank correlation",
        "feedback": "It correlates ranks and describes the strength of monotonic association."
      },
      {
        "id": "C",
        "text": "Intraclass correlation for agreement",
        "feedback": "It targets agreement or reliability under a specified measurement model."
      },
      {
        "id": "D",
        "text": "Coefficient of variation",
        "feedback": "It is a relative measure of a single variable\u0027s dispersion."
      },
      {
        "id": "E",
        "text": "Mean paired difference",
        "feedback": "It describes an average change or difference in measurement level."
      }
    ],
    "correct": "B",
    "hint": "The question asks about ranked order, without requiring a straight line.",
    "lesson": "A monotonic relationship can be curved and still preserve ordering.",
    "explanation": "Spearman correlation addresses the requested rank-based association. A strong correlation does not establish agreement or a causal effect.",
    "lessonId": "choose-analysis",
    "sourceIds": [
      "dfph-syllabus"
    ],
    "help": "nonparametric_methods/spearman.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "57a614caa3b6ce466ccf19b7a9bca3f388e9beda5eec689961dc2b00e2cbef46"
    }
  },
  {
    "id": "BIO-SURV-01",
    "version": 1,
    "track": "publicHealth",
    "topic": "Surveillance ascertainment",
    "objective": "Identify the information needed to interpret a detection signal.",
    "stem": "A surveillance system records twice as many positive reports this month after several additional practices begin submitting specimens. Which response is most informative before interpreting the rise as a change in incidence?",
    "options": [
      {
        "id": "A",
        "text": "Compare raw counts with the previous month only",
        "feedback": "Changes in participating practices and reporting can affect raw counts."
      },
      {
        "id": "B",
        "text": "Divide the new positives by last month\u0027s number of tests",
        "feedback": "Misaligned time denominators give an uninterpretable measure."
      },
      {
        "id": "C",
        "text": "Compare positivity and reports using comparable practices and periods, and inspect testing coverage and case definitions",
        "feedback": "This helps distinguish changes in surveillance from a genuine rise."
      },
      {
        "id": "D",
        "text": "Check whether the population census doubled",
        "feedback": "Population size is relevant, but this ignores the known surveillance change."
      },
      {
        "id": "E",
        "text": "Assume incidence is stable because ascertainment changed",
        "feedback": "A genuine rise can occur at the same time as changed ascertainment."
      }
    ],
    "correct": "C",
    "hint": "Ask what changed about how cases enter the system.",
    "lesson": "Reporting systems can change the number of detected cases independently of underlying disease occurrence.",
    "explanation": "Use like-for-like time windows and coverage, compare testing denominators and case definitions, and triangulate with other indicators where possible.",
    "lessonId": "surveillance-outbreaks",
    "sourceIds": [
      "gmc-graduates",
      "phe-surveillance"
    ],
    "help": "clinical_epidemiology/risk_prospective.htm",
    "difficulty": "beginner",
    "learnerGoals": [
      "undergraduateMedicine",
      "publicHealth"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "f20ca59c78104ab88d32b0dd1ae8cc5163a10cc0ae8deba144b640532607d567"
    }
  },
  {
    "id": "BIO-SURV-02",
    "version": 1,
    "track": "publicHealth",
    "topic": "Outbreak cohort analysis",
    "objective": "Calculate a risk ratio from known attendees.",
    "stem": "At a fictional residential course, all 80 participants were followed for three days. Illness affected 12 of 30 who used a shared water dispenser and 5 of 50 who did not. What is the observed three-day illness risk ratio for dispenser use versus no use?",
    "options": [
      {
        "id": "A",
        "text": "0.25",
        "feedback": "This is the reciprocal comparison."
      },
      {
        "id": "B",
        "text": "0.30",
        "feedback": "This is the risk difference as a fraction, equal to 30 percentage points."
      },
      {
        "id": "C",
        "text": "2.4",
        "feedback": "The ratio of case counts ignores the different denominators."
      },
      {
        "id": "D",
        "text": "4.0",
        "feedback": "The two group risks are 0.40 and 0.10, so their ratio is 4."
      },
      {
        "id": "E",
        "text": "6.0",
        "feedback": "This is the odds ratio, using cases and non-cases as the odds denominators."
      }
    ],
    "correct": "D",
    "hint": "Calculate the risk within each exposure group before dividing.",
    "lesson": "Whole-group denominators produce risks in a completed fixed cohort.",
    "explanation": "12/30 divided by 5/50 is 4. This is an observed association; competing exposures and ascertainment should be checked.",
    "lessonId": "surveillance-outbreaks",
    "sourceIds": [
      "dfph-syllabus",
      "ukhsa-outbreak"
    ],
    "help": "clinical_epidemiology/risk_prospective.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "d624e787ba5a585e20518288d2027b0f7aaeeea61902bea7310b537bd4a39a0c"
    }
  },
  {
    "id": "BIO-SURV-03",
    "version": 1,
    "track": "publicHealth",
    "topic": "Ecological comparison",
    "objective": "Identify what district-level data can support.",
    "stem": "Districts with higher air-pollution averages have higher age-standardised hospital admission rates. The analyst has district averages and rates only. Which conclusion is most defensible?",
    "options": [
      {
        "id": "A",
        "text": "The district-level association is observable, while individual exposure\u2013outcome association and causality require additional data and assumptions",
        "feedback": "Aggregate associations do not identify relationships for individuals."
      },
      {
        "id": "B",
        "text": "Age standardisation establishes that pollution caused the admissions",
        "feedback": "Adjusting for age structure does not handle every confounder or bias."
      },
      {
        "id": "C",
        "text": "Everyone in a higher-pollution district has higher personal admission risk",
        "feedback": "District means do not describe each individual or their risk."
      },
      {
        "id": "D",
        "text": "The association is necessarily the same within every age group",
        "feedback": "A standardised aggregate does not establish uniform stratum-specific associations."
      },
      {
        "id": "E",
        "text": "District-level associations are always spurious",
        "feedback": "They describe aggregate patterns and may be relevant for planning and hypothesis formation."
      }
    ],
    "correct": "A",
    "hint": "Identify the unit on which exposure and outcome were observed.",
    "lesson": "Population-level evidence and individual-level inference answer different questions.",
    "explanation": "The data describe districts and can motivate further study. Individual association and causal claims need an appropriate design, linked observations and control of relevant biases.",
    "lessonId": "surveillance-outbreaks",
    "sourceIds": [
      "gmc-graduates",
      "dfph-syllabus"
    ],
    "help": "clinical_epidemiology/risk_prospective.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "publicHealth",
      "healthcareBusinessAnalyst",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "13dddd2c0769b58a81bd4fbe551827842e3e983a46ab2f0d06c2e542779a6a96"
    }
  },
  {
    "id": "BIO-DEP-01",
    "version": 1,
    "track": "publicHealth",
    "topic": "Cluster design effect",
    "objective": "Calculate the approximate standard-error adjustment.",
    "stem": "A fictional survey samples equal clusters of 10 people. Suppose the common intracluster correlation is 0.20 and use the simple equal-size approximation. What should multiply the independent-sample standard error?",
    "options": [
      {
        "id": "A",
        "text": "0.36",
        "feedback": "A factor below one would reduce uncertainty despite the positive correlation under the stated design."
      },
      {
        "id": "B",
        "text": "1.00",
        "feedback": "This treats all observed people as independent."
      },
      {
        "id": "C",
        "text": "1.67",
        "feedback": "The variance ratio is 1 \u002B 9 \u00D7 0.20 = 2.8. Its square root is about 1.67."
      },
      {
        "id": "D",
        "text": "2.80",
        "feedback": "This is the variance multiplier, not the standard-error multiplier."
      },
      {
        "id": "E",
        "text": "3.00",
        "feedback": "Using cluster size rather than cluster size minus one is incorrect; this would also be a variance multiplier."
      }
    ],
    "correct": "C",
    "hint": "Compute the design effect first and recall how variance relates to standard error.",
    "lesson": "A standard error is the square root of a variance.",
    "explanation": "The approximate design effect is 2.8; the standard-error multiplier is its square root, about 1.67.",
    "lessonId": "dependent-samples",
    "sourceIds": [
      "dfph-syllabus",
      "ons-survey-error"
    ],
    "help": "sample_size/population_survey.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "publicHealth",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "d018dbd2bdccda673a9fc38ac2339af01ee294ab4e335b366c0031305733a163"
    }
  },
  {
    "id": "BIO-DEP-02",
    "version": 1,
    "track": "publicHealth",
    "topic": "Survey weights and variance",
    "objective": "Identify design inputs for a national estimate.",
    "stem": "A health survey oversamples rural areas and interviews several adults per sampled household. It provides design weights, strata and household identifiers as primary sampling units. How should a population proportion and its standard error be estimated?",
    "options": [
      {
        "id": "A",
        "text": "Use an unweighted proportion with binomial SE",
        "feedback": "This ignores unequal selection and dependence."
      },
      {
        "id": "B",
        "text": "Use an unweighted proportion and double its SE",
        "feedback": "An arbitrary multiplier does not account for either selection or the sample design."
      },
      {
        "id": "C",
        "text": "Use the weights but use a binomial SE with the raw headcount",
        "feedback": "Weights can correct the point estimate; raw binomial SE ignores complex design."
      },
      {
        "id": "D",
        "text": "Analyse the number of households as though it were the count of adults",
        "feedback": "Sampling units and outcome units need to be represented appropriately."
      },
      {
        "id": "E",
        "text": "Use survey weights for estimation and the supplied strata and primary sampling units in design-based variance estimation",
        "feedback": "This uses both the weighting and relevant design information."
      }
    ],
    "correct": "E",
    "hint": "Think separately about who the sample represents and how its observations are related.",
    "lesson": "Point estimation and variance estimation use different aspects of the survey design.",
    "explanation": "The supplied weights and design variables allow estimation for the target population while accounting for unequal selection, strata and clustering.",
    "lessonId": "dependent-samples",
    "sourceIds": [
      "ons-survey-design",
      "ons-survey-error"
    ],
    "help": "sample_size/population_survey.htm",
    "difficulty": "advanced",
    "learnerGoals": [
      "publicHealth",
      "healthcareBusinessAnalyst",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "13adb037c412bd4db8c6e9e047a38b6088d4080fe52f21b69a056b039157beb6"
    }
  },
  {
    "id": "BIO-DEP-03",
    "version": 1,
    "track": "medicine",
    "topic": "Repeated measurements",
    "objective": "Recognise why repeated measurements are dependent.",
    "stem": "A study records four blood pressure readings for each of 60 participants over a year. It aims to estimate the mean trajectory using all readings. Which strategy best recognises the data structure?",
    "options": [
      {
        "id": "A",
        "text": "Fit a longitudinal model with participant-level dependence accounted for",
        "feedback": "It can use all observations while allowing readings from one participant to be correlated."
      },
      {
        "id": "B",
        "text": "Use an independent-observation model with 240 independent patients",
        "feedback": "The study has 60 independent participant units if participants are sampled independently."
      },
      {
        "id": "C",
        "text": "Apply a single paired t test to all 240 rows",
        "feedback": "A two-occasion paired test does not describe a four-occasion trajectory."
      },
      {
        "id": "D",
        "text": "Keep the single highest reading for each person",
        "feedback": "Maximum readings answer a different question and discard timing."
      },
      {
        "id": "E",
        "text": "Treat the four time points as independent groups and drop participant identifiers",
        "feedback": "Removing identifiers does not remove dependence."
      }
    ],
    "correct": "A",
    "hint": "What do the four rows for each participant have in common?",
    "lesson": "Repeated observations improve information about trajectories but share a participant.",
    "explanation": "A suitable longitudinal analysis models or accommodates within-participant dependence and uses time appropriately; choose the model after defining the trajectory of interest.",
    "lessonId": "dependent-samples",
    "sourceIds": [
      "dfph-syllabus",
      "ons-survey-error"
    ],
    "help": "sample_size/population_survey.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "dc86cf1958e6e8e18c9800b3c6bf36c696b471d3ba1d1974eef9cc3cf0ffcd43"
    }
  },
  {
    "id": "CLIN-POWER-01",
    "version": 2,
    "track": "medicine",
    "topic": "Recruitment allowance",
    "objective": "Apply a loss allowance to a per-arm sample size.",
    "stem": "A fictional two-arm trial needs 108 analysable participants in each arm. Investigators expect 15% of recruited participants to have no primary outcome in each arm. Based on this allowance alone, what is the minimum integer recruitment target per arm?",
    "options": [
      {
        "id": "A",
        "text": "108",
        "feedback": "This is the number needed for analysis and does not allow for loss."
      },
      {
        "id": "B",
        "text": "124",
        "feedback": "Adding 15% of the analysable count underestimates loss, which is expected among those recruited."
      },
      {
        "id": "C",
        "text": "126",
        "feedback": "This target leaves 107.1 expected analysable participants per arm."
      },
      {
        "id": "D",
        "text": "128",
        "feedback": "Dividing 108 by 0.85 gives approximately 127.06, which rounds up to 128."
      },
      {
        "id": "E",
        "text": "255",
        "feedback": "255 comes from rounding up the combined calculation 216/0.85. The target is per arm, so round up 108/0.85 separately for each arm."
      }
    ],
    "correct": "D",
    "hint": "If N is recruited, the expected number available is 0.85 \u00D7 N.",
    "lesson": "Inflate the analysable target by dividing by the fraction expected to remain.",
    "explanation": "Require 0.85 \u00D7 N \u2265 108, so round 108/0.85 up to 128 per arm. This is an expected-count allowance and does not address informative attrition.",
    "help": "sample_size/unpaired_t_test.htm",
    "lessonId": "trial-power",
    "sourceIds": [
      "ich-e9",
      "rcog-mrcog"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "6140f7b6226e9c152b7ca469be23df805744cf0ab8c3df752d8a076d11ca42d3"
    }
  },
  {
    "id": "CLIN-POWER-02",
    "version": 1,
    "track": "medicine",
    "topic": "Type II error",
    "objective": "Give the correct conditional interpretation of beta.",
    "stem": "A superiority trial design has 80% power if the true improvement difference is 3 points under its specified assumptions. Which repeated-trial frequency follows from this statement?",
    "options": [
      {
        "id": "A",
        "text": "About 20% of repeats fail to reject the null when the true difference is 3 points.",
        "feedback": "Correct. This is beta at the specified alternative."
      },
      {
        "id": "B",
        "text": "About 20% of statistically significant trial reports are false positives.",
        "feedback": "Power does not give the proportion of significant results that arise from true nulls."
      },
      {
        "id": "C",
        "text": "About 20% of repeats reject the null when it is true.",
        "feedback": "That probability is the type I error at the null, governed by alpha."
      },
      {
        "id": "D",
        "text": "About 80% of patients improve by at least 3 points.",
        "feedback": "Power concerns a decision across repeated trials, not individual response rates."
      },
      {
        "id": "E",
        "text": "About 80% of trials with any nonzero difference reject the null.",
        "feedback": "Power varies with the magnitude and direction of the true difference."
      }
    ],
    "correct": "A",
    "hint": "Identify what is assumed true and which decision counts as an error.",
    "lesson": "At a chosen alternative, beta is the probability the planned test fails to reject the null.",
    "explanation": "With the true difference fixed at 3 points, beta is 1 \u2212 0.80 = 0.20 under the specified model.",
    "help": "sample_size/unpaired_t_test.htm",
    "lessonId": "trial-power",
    "sourceIds": [
      "ich-e9",
      "rcpsych-paperb"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "cd682852b69e7d77ee9035a3364c26e7858a2b80e887582f303d69110161ba68"
    }
  },
  {
    "id": "CLIN-POWER-03",
    "version": 1,
    "track": "medicine",
    "topic": "Trial margins",
    "objective": "Distinguish three confidence-interval decision rules.",
    "stem": "In a fictional trial, higher scores are better. The estimated new-minus-control difference has a 95% confidence interval of \u22121.1 to \u002B3.0 points. Use the same interval to assess prespecified rules: superiority requires the whole interval above 0, noninferiority requires the lower bound above \u22122, and equivalence requires the whole interval between \u22122 and \u002B2. Which rule is met?",
    "options": [
      {
        "id": "A",
        "text": "Superiority only",
        "feedback": "The lower bound is below zero, so the superiority rule is not met."
      },
      {
        "id": "B",
        "text": "Equivalence only",
        "feedback": "The upper bound exceeds \u002B2, outside the equivalence range."
      },
      {
        "id": "C",
        "text": "Noninferiority only",
        "feedback": "The lower bound exceeds \u22122. The interval crosses zero and exceeds the upper equivalence margin."
      },
      {
        "id": "D",
        "text": "All three rules",
        "feedback": "Only the noninferiority boundary is cleared."
      },
      {
        "id": "E",
        "text": "None of the rules",
        "feedback": "The lower bound is \u22121.1, above the noninferiority boundary."
      }
    ],
    "correct": "C",
    "hint": "Compare the lower limit with \u22122 and zero, then inspect the upper limit.",
    "lesson": "Each claim uses a different region of effects to exclude.",
    "explanation": "The interval excludes losses of 2 points or more under the stated confidence rule. It neither excludes zero nor rules out a difference greater than \u002B2.",
    "help": "sample_size/unpaired_t_test.htm",
    "lessonId": "trial-power",
    "sourceIds": [
      "ich-e9",
      "fda-ni"
    ],
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "5ac34ba3efdc5c112148ca5e360b6c50b4ff3d5cdcc3c1328ebbc99a2565efcf"
    }
  },
  {
    "id": "CLIN-TRIAL-01",
    "version": 1,
    "track": "medicine",
    "topic": "Recruitment and allocation",
    "objective": "Identify the process that prevents selective enrolment.",
    "stem": "A trial compares two rehabilitation schedules. A computer creates the random allocation sequence, but the enrolment therapist can see the next assignment before confirming a patient\u0027s eligibility and consent. Which design change most directly addresses this weakness?",
    "options": [
      {
        "id": "A",
        "text": "Ask participants not to tell the outcome assessor which schedule they receive.",
        "feedback": "This protects outcome assessment after assignment, leaving the recruitment problem."
      },
      {
        "id": "B",
        "text": "Use more centres without changing enrolment.",
        "feedback": "The same foreknowledge can influence enrolment in any number of centres."
      },
      {
        "id": "C",
        "text": "Make the allocation list available only to the enrolment therapist.",
        "feedback": "That therapist can still alter enrolment after seeing the assignment."
      },
      {
        "id": "D",
        "text": "Adjust the results for recorded age and baseline severity.",
        "feedback": "Adjustment is not a substitute for preventing selective enrolment, including selection on unmeasured factors."
      },
      {
        "id": "E",
        "text": "Use a secure service to reveal the next assignment only after eligibility and consent are recorded.",
        "feedback": "Correct. This conceals forthcoming assignments until enrolment is fixed."
      }
    ],
    "correct": "E",
    "hint": "Ask what the recruiter can know before the participant is enrolled.",
    "lesson": "Allocation concealment occurs before assignment. Blinding refers to knowledge after assignment.",
    "explanation": "Recruiters who can see the next assignment can selectively enrol or delay patients. A secure service releasing it only after enrolment limits that opportunity.",
    "help": "randomization/randomization.htm",
    "lessonId": "trial-appraisal",
    "sourceIds": [
      "consort-2025",
      "rcpsych-paperb"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "undergraduateMedicine",
      "postgraduateMedicine",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "520a7b0202bb58d08e11d01446b65b3a34ffa501e5b0a858311f3000bdd4c436"
    }
  },
  {
    "id": "CLIN-TRIAL-02",
    "version": 1,
    "track": "medicine",
    "topic": "Trial attrition",
    "objective": "Appraise an available-case analysis that claims intention to treat.",
    "stem": "A randomised trial of home monitoring reports fewer admissions in the intervention group, analysing everyone with recorded outcomes in their assigned group. Outcome data are missing in 22% of the intervention group and 3% of controls. The paper calls the result intention to treat. What is the most useful next check?",
    "options": [
      {
        "id": "A",
        "text": "Assume missing participants had no admission in both groups.",
        "feedback": "This replaces unknown outcomes with an assumption that may be especially misleading when follow-up differs."
      },
      {
        "id": "B",
        "text": "Review reasons for missing outcomes by arm and sensitivity analyses for the prespecified endpoint.",
        "feedback": "Correct. Original group labels are useful, but informative missingness may still bias the comparison."
      },
      {
        "id": "C",
        "text": "Move participants with missing outcomes into the control group.",
        "feedback": "This violates original allocation and does not recover any missing observations."
      },
      {
        "id": "D",
        "text": "Compare the P value with 0.05 before considering follow-up.",
        "feedback": "A P value from incomplete data does not resolve the missingness."
      },
      {
        "id": "E",
        "text": "Check only that the allocation sequence was generated randomly.",
        "feedback": "Sequence generation does not explain why outcome ascertainment differs after assignment."
      }
    ],
    "correct": "B",
    "hint": "Does analysing observed outcomes by allocation supply data for the people who are absent?",
    "lesson": "A treatment-policy comparison still needs follow-up and a defensible missing-data plan.",
    "explanation": "Identify the amount, causes and timing of missingness by group. Assess whether clinically plausible assumptions about absent outcomes change the estimate.",
    "help": "randomization/randomization.htm",
    "lessonId": "trial-appraisal",
    "sourceIds": [
      "consort-2025",
      "ich-e9"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "undergraduateMedicine",
      "postgraduateMedicine",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "aa61308481c6d9b6bd53c45bdb2bfef4345a9932e5a182909ff40c7b14d083f3"
    }
  },
  {
    "id": "CLIN-TRIAL-03",
    "version": 2,
    "track": "medicine",
    "topic": "Clustered allocation",
    "objective": "Recognise the consequence of ward-level randomisation.",
    "stem": "A trial randomises 16 wards to a new discharge protocol or usual care. Patients\u0027 readmissions are then recorded. An analysis calculates its standard error as if all patients had been independently randomised. What is the central problem?",
    "options": [
      {
        "id": "A",
        "text": "Treatments must be assigned by alternating patients across wards.",
        "feedback": "Alternation is predictable and does not address within-ward dependence."
      },
      {
        "id": "B",
        "text": "Patient outcomes cannot be recorded in a cluster trial.",
        "feedback": "Individual outcomes are useful. The analysis must reflect how patients are grouped."
      },
      {
        "id": "C",
        "text": "The outcome must be converted to a continuous score.",
        "feedback": "Binary outcomes are valid for cluster trials."
      },
      {
        "id": "D",
        "text": "The analysis ignores ward-level allocation and possible within-ward correlation.",
        "feedback": "The allocation units are wards. Account for possible correlation among patients within each ward and use inference appropriate for 16 wards."
      },
      {
        "id": "E",
        "text": "Every patient\u0027s pre-discharge and post-discharge measurements should be treated as a crossover.",
        "feedback": "Receiving a ward protocol does not constitute receiving both treatments in random order."
      }
    ],
    "correct": "D",
    "hint": "Identify what was randomised and which observations share local influences.",
    "lesson": "People in the same randomised cluster may have related outcomes, so the apparent sample size can exaggerate precision.",
    "explanation": "An analysis that ignores within-ward correlation can underestimate uncertainty. The allocation unit is the ward. Use a method that accounts for correlation among patients within wards and gives appropriate inference for 16 independent wards.",
    "help": "randomization/randomization.htm",
    "lessonId": "trial-appraisal",
    "sourceIds": [
      "consort-2025",
      "rcpsych-paperb"
    ],
    "difficulty": "advanced",
    "learnerGoals": [
      "undergraduateMedicine",
      "postgraduateMedicine",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "7e63efc22c8244980808456cf53eb69da9625854230a0b2a119ac95f61028593"
    }
  },
  {
    "id": "CLIN-DX-01",
    "version": 2,
    "track": "medicine",
    "topic": "Negative test result",
    "objective": "Convert a negative likelihood ratio to a remaining probability.",
    "stem": "A fictional test has sensitivity 75% and specificity 80% at a fixed threshold. A patient\u0027s pretest probability of the condition is 40%. Assuming these estimates apply to the patient, what is the approximate probability after a negative result?",
    "options": [
      {
        "id": "A",
        "text": "12.5%",
        "feedback": "12.5% comes from multiplying the pretest probability by 0.3125. The likelihood ratio multiplies the pretest odds."
      },
      {
        "id": "B",
        "text": "17.2%",
        "feedback": "Correct. Prior odds 2/3 multiplied by 0.25/0.80 give 5/24. Convert to probability 5/29."
      },
      {
        "id": "C",
        "text": "25.0%",
        "feedback": "This is the proportion of people with the condition who test negative, not the chance of disease after a negative test."
      },
      {
        "id": "D",
        "text": "31.3%",
        "feedback": "31.3% expresses LR\u2212 = 0.3125 as a rounded percentage. Calculate post-test odds before converting back to probability."
      },
      {
        "id": "E",
        "text": "68.8%",
        "feedback": "68.8% expresses 1 \u2212 LR\u2212 = 0.6875 as a rounded percentage. Taking that complement does not update the pretest probability."
      }
    ],
    "correct": "B",
    "hint": "Use the false-negative fraction divided by specificity, and multiply odds rather than probability.",
    "lesson": "A negative result lowers the odds when its LR is below 1. Convert the remaining odds to a probability before communicating it.",
    "explanation": "The negative LR is 0.25/0.80 = 0.3125. Prior odds are 0.40/0.60 = 2/3. The updated probability is (5/24)/(1 \u002B 5/24), or about 17.2%.",
    "help": "nonparametric_methods/roc.htm",
    "lessonId": "diagnostic-evidence",
    "sourceIds": [
      "fda-diagnostic",
      "rcgp-evidence"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "undergraduateMedicine",
      "postgraduateMedicine",
      "usMedicalBoards"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "0035d6a349c3b87ad2736a33397f8cfcd9d1059bf3a75ee0e0a1fbcf35594423"
    }
  },
  {
    "id": "CLIN-DX-02",
    "version": 2,
    "track": "medicine",
    "topic": "Partial verification",
    "objective": "Spot a design feature that can bias accuracy estimates.",
    "stem": "A clinic compares a new clot marker with imaging. Nearly everyone with a positive marker receives imaging, but most people with a negative marker do not. Investigators report accuracy only for those imaged. Which limitation follows most directly?",
    "options": [
      {
        "id": "A",
        "text": "The results have no clinically relevant threshold.",
        "feedback": "The marker can have a threshold regardless of who receives imaging."
      },
      {
        "id": "B",
        "text": "The verified subset automatically has the intended clinic prevalence.",
        "feedback": "Selection for imaging depends on the marker result, so its case fraction is not automatically representative."
      },
      {
        "id": "C",
        "text": "A larger sample would remove bias from selective verification.",
        "feedback": "Larger samples cannot remove systematic selection into verification."
      },
      {
        "id": "D",
        "text": "The design proves the imaging itself causes positive marker results.",
        "feedback": "Imaging determines the reference classification, but the problem described is selective verification."
      },
      {
        "id": "E",
        "text": "Disease status is incompletely established among marker-negative people, which can bias reported accuracy.",
        "feedback": "This is partial verification. Disease status is missing for many marker-negative participants because reference verification depends on the index result."
      }
    ],
    "correct": "E",
    "hint": "Who does the accuracy table omit, and is that related to their marker result?",
    "lesson": "Check how the intended diagnostic population was enrolled and which patients received the reference standard.",
    "explanation": "The subset with reference results is selected partly by the index result. Unverified negative patients can include missed cases. The available table cannot simply be treated as if it covered all tested people.",
    "help": "nonparametric_methods/roc.htm",
    "lessonId": "diagnostic-evidence",
    "sourceIds": [
      "stard-2015",
      "fda-diagnostic"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "undergraduateMedicine",
      "postgraduateMedicine",
      "usMedicalBoards"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "98f800cb975c5f6af2e0453d3702281d5b668aaea14c99f72764b4f19611235f"
    }
  },
  {
    "id": "CLIN-DX-03",
    "version": 1,
    "track": "medicine",
    "topic": "ROC discrimination",
    "objective": "Recognise what discrimination does not tell us about an operating threshold.",
    "stem": "A fictional marker\u0027s empirical ROC AUC is 0.84, estimated by comparing scores from known cases with known noncases and counting ties as halves. Which interpretation is supported by this value alone?",
    "options": [
      {
        "id": "A",
        "text": "At the selected threshold, 84% of positive results are true positives.",
        "feedback": "That is positive predictive value at a specified cut-off, which also depends on the case mix."
      },
      {
        "id": "B",
        "text": "Any threshold on the curve has 84% sensitivity.",
        "feedback": "Sensitivity varies with threshold."
      },
      {
        "id": "C",
        "text": "The marker\u0027s predicted probabilities match observed disease frequencies.",
        "feedback": "That is calibration. Ranking performance alone does not establish it."
      },
      {
        "id": "D",
        "text": "It scores a randomly selected case higher than a randomly selected noncase about 84% of the time when ties count as half credit.",
        "feedback": "Correct. This describes ranking in the evaluated data."
      },
      {
        "id": "E",
        "text": "The optimal treatment decision for each patient follows from 0.84 alone.",
        "feedback": "Clinical utility depends on threshold, setting and consequences of decisions."
      }
    ],
    "correct": "D",
    "hint": "AUC gives a summary of pairwise ranking across thresholds.",
    "lesson": "AUC describes discrimination in the evaluated population. It does not establish calibration, predictive value or the best decision rule.",
    "explanation": "The empirical AUC equals the fraction of case\u2013noncase pairs correctly ordered plus half the tied fraction. Performance in another population needs separate evidence.",
    "help": "nonparametric_methods/roc.htm",
    "lessonId": "diagnostic-evidence",
    "sourceIds": [
      "fda-diagnostic",
      "stard-2015"
    ],
    "difficulty": "advanced",
    "learnerGoals": [
      "undergraduateMedicine",
      "postgraduateMedicine",
      "usMedicalBoards"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "877f6944393418743df6178fc3fbeb46aab1350bde2b0f229cedd4fe74644c26"
    }
  },
  {
    "id": "CLIN-SCREEN-01",
    "version": 1,
    "track": "publicHealth",
    "topic": "Survival and lead time",
    "objective": "Recognise lead time in an individual example with unchanged death age.",
    "stem": "For a fictional condition, screening shifts a person\u0027s date of diagnosis from age 67 to 64. Treatment and age at death remain identical. Which phenomenon can explain the apparent three-year increase in survival measured from diagnosis?",
    "options": [
      {
        "id": "A",
        "text": "Lead-time bias",
        "feedback": "Correct. The diagnosis moves earlier but the death does not."
      },
      {
        "id": "B",
        "text": "Length bias",
        "feedback": "Length bias concerns preferential detection of more slowly progressing conditions. This example follows one unchanged course."
      },
      {
        "id": "C",
        "text": "Verification bias",
        "feedback": "The example does not concern selective application of a reference standard."
      },
      {
        "id": "D",
        "text": "Loss to follow-up",
        "feedback": "Death is observed at the same age under both scenarios."
      },
      {
        "id": "E",
        "text": "A treatment effect on mortality",
        "feedback": "The example explicitly fixes the time of death."
      }
    ],
    "correct": "A",
    "hint": "Which event changed, diagnosis or death?",
    "lesson": "Looking from diagnosis starts the clock at different times. A programme comparison needs a common start point.",
    "explanation": "The added three years reflect earlier recognition. No extension of life follows from comparing survival from diagnosis alone.",
    "help": "clinical_epidemiology/screening_test.htm",
    "lessonId": "screening-programmes",
    "sourceIds": [
      "uk-nsc-effectiveness",
      "uk-nsc-glossary"
    ],
    "difficulty": "beginner",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "undergraduateMedicine"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "b1d930c0e2b2a6b9425adcd7c8c0bb278cd32c309f3a5e34dbe9f020d162e265"
    }
  },
  {
    "id": "CLIN-SCREEN-02",
    "version": 1,
    "track": "publicHealth",
    "topic": "Overdiagnosis",
    "objective": "Distinguish overdiagnosis from an incorrect result or a false alarm.",
    "stem": "A fictional screening test triggers a biopsy. The biopsy correctly identifies a slow-growing lesion. In the hypothetical absence of screening, the lesion would never have caused symptoms or shortened the person\u0027s life. Which term best describes detecting it?",
    "options": [
      {
        "id": "A",
        "text": "False-positive screening result",
        "feedback": "There is a real lesion, confirmed by diagnostic assessment."
      },
      {
        "id": "B",
        "text": "Lead-time bias alone",
        "feedback": "An earlier diagnosis can affect observed survival, but the defining feature here is that the lesion would never matter clinically."
      },
      {
        "id": "C",
        "text": "Overdiagnosis",
        "feedback": "Correct. The disease is genuinely detected, but would never have caused harm in the person\u0027s lifetime."
      },
      {
        "id": "D",
        "text": "False-negative screening result",
        "feedback": "The lesion was detected."
      },
      {
        "id": "E",
        "text": "Prevented clinical disease",
        "feedback": "No symptoms would have occurred even without detection."
      }
    ],
    "correct": "C",
    "hint": "Does the finding exist, and would it have affected the person\u0027s life without screening?",
    "lesson": "Overdiagnosis concerns the consequences of detecting real disease. It is distinct from a false positive.",
    "explanation": "The hypothetical natural history meets the definition of overdiagnosis. In practice, whether an individual case is overdiagnosed is usually uncertain.",
    "help": "clinical_epidemiology/screening_test.htm",
    "lessonId": "screening-programmes",
    "sourceIds": [
      "uk-nsc-effectiveness",
      "uk-nsc-glossary"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "undergraduateMedicine"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "80d38cf49016395c843f444dd66f5438e0254cfb7e5a0a7bfdb0bdad5d002da8"
    }
  },
  {
    "id": "CLIN-SCREEN-03",
    "version": 2,
    "track": "publicHealth",
    "topic": "Perinatal mortality",
    "objective": "Identify the numerator, denominator and scaling of a maternity indicator.",
    "stem": "A fictional annual register uses one consistent definition. It records 9,960 live births, 40 stillbirths, 10 deaths before seven completed days of life and 2 maternal deaths meeting the maternal mortality definition. What is the perinatal mortality rate per 1,000 total births, to two decimal places?",
    "options": [
      {
        "id": "A",
        "text": "0.20 per 1,000",
        "feedback": "This uses maternal deaths, which do not belong in the perinatal numerator."
      },
      {
        "id": "B",
        "text": "4.00 per 1,000",
        "feedback": "This uses stillbirths only, omitting early neonatal deaths."
      },
      {
        "id": "C",
        "text": "5.02 per 1,000",
        "feedback": "That is approximately 50/9,960 \u00D7 1,000. Perinatal mortality uses total births."
      },
      {
        "id": "D",
        "text": "5.00 per 1,000",
        "feedback": "Correct. The 50 perinatal deaths are divided by 10,000 total births, then multiplied by 1,000."
      },
      {
        "id": "E",
        "text": "50.00 per 1,000",
        "feedback": "This is the death count without the specified scaling and denominator."
      }
    ],
    "correct": "D",
    "hint": "Combine stillbirths and early neonatal deaths, and include stillbirths in the denominator.",
    "lesson": "Perinatal mortality and maternal mortality use different events and denominator definitions.",
    "explanation": "The numerator is 40 \u002B 10 and the denominator is 9,960 \u002B 40. This gives 5.00 per 1,000 total births. The maternal deaths belong to a separate indicator.",
    "help": "clinical_epidemiology/screening_test.htm",
    "lessonId": "screening-programmes",
    "sourceIds": [
      "ons-perinatal",
      "who-mmr",
      "rcog-mrcog"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "undergraduateMedicine"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "7a25327da344a3939b7b0e89492890423313ae4a849110790e26be5030a34c7f"
    }
  },
  {
    "id": "SURVIVAL-KM-01",
    "version": 1,
    "track": "medicine",
    "topic": "Kaplan\u2013Meier risk sets",
    "objective": "Update survival after right censoring.",
    "stem": "Ten patients are initially unrecovered. Two recover on day 1. Two other patients stop being observed on day 2 while still unrecovered. One recovers on day 3. The rest are still observed. What is the Kaplan\u2013Meier estimate of remaining unrecovered just after day 3?",
    "options": [
      {
        "id": "A",
        "text": "0.50",
        "feedback": "That treats all five exits as events."
      },
      {
        "id": "B",
        "text": "0.70",
        "feedback": "Three events divided by ten ignores the later risk set."
      },
      {
        "id": "C",
        "text": "0.667",
        "feedback": "The two survival factors are 8/10 and 5/6."
      },
      {
        "id": "D",
        "text": "0.75",
        "feedback": "There were six still at risk at the last event."
      },
      {
        "id": "E",
        "text": "0.833",
        "feedback": "5/6 is the conditional day 3 factor alone."
      }
    ],
    "correct": "C",
    "hint": "Write the risk set just before each event time.",
    "lesson": "Define the start of follow-up, event and censoring. A right-censored participant contributes observed time at risk. Kaplan\u2013Meier multiplies conditional probabilities of remaining free of the specified event. Death before relapse prevents first relapse and enters its real-world cumulative incidence as a competing event. Log-rank compares curves using observed and expected event counts. A Cox hazard ratio describes relative instantaneous event rates. Its usual constant summary assumes proportional hazards.",
    "explanation": "(1 \u2212 2/10) \u00D7 (1 \u2212 1/6) = 2/3. The two censorings reduce the later risk set.",
    "operation": "KaplanMeier",
    "help": "survival_analysis/kaplan_meier.htm",
    "lessonId": "survival-competing-risks",
    "sourceIds": [
      "survival-vignette"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "d6b3e72502f577aceb922b2c0b5686ac406209960d005c08a4dc3378b44679f9"
    }
  },
  {
    "id": "SURVIVAL-CR-01",
    "version": 2,
    "track": "researcher",
    "topic": "Competing events",
    "objective": "Choose an observed-world probability estimator.",
    "stem": "An oncology team wants the probability that a first documented relapse occurs within one year of starting follow-up. A patient who dies relapse-free cannot subsequently relapse. Some are lost to follow-up alive and relapse-free. Which analysis best answers the question?",
    "options": [
      {
        "id": "A",
        "text": "Censor deaths and report 1 minus Kaplan\u2013Meier.",
        "feedback": "It can overestimate relapse probability by treating death as ordinary loss of observation."
      },
      {
        "id": "B",
        "text": "Report the cause-specific relapse hazard at one year.",
        "feedback": "That is an instantaneous rate."
      },
      {
        "id": "C",
        "text": "Report first relapse or death as one endpoint.",
        "feedback": "This gives the risk of a composite outcome."
      },
      {
        "id": "D",
        "text": "Estimate relapse cumulative incidence with pre-relapse death as a competing event.",
        "feedback": "This accounts for the competing event that prevents a future first relapse."
      },
      {
        "id": "E",
        "text": "Exclude everyone who dies before relapse at the start.",
        "feedback": "That conditions selection on a future outcome."
      }
    ],
    "correct": "D",
    "hint": "Identify which event prevents a future first relapse.",
    "lesson": "Define the start of follow-up, event and censoring. A right-censored participant contributes observed time at risk. Kaplan\u2013Meier multiplies conditional probabilities of remaining free of the specified event. Death before relapse prevents first relapse and enters its real-world cumulative incidence as a competing event. Log-rank compares curves using observed and expected event counts. A Cox hazard ratio describes relative instantaneous event rates. Its usual constant summary assumes proportional hazards.",
    "explanation": "Use a cumulative incidence estimator for first relapse with death before relapse as a competing event. Handle loss of follow-up as right censoring under suitable assumptions.",
    "operation": "",
    "help": "survival_analysis/kaplan_meier.htm",
    "lessonId": "survival-competing-risks",
    "sourceIds": [
      "survival-vignette",
      "medical-oncology-sce"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "82453b0ce726a9564849a3278ec6f784da4c83dcd7614c2b870f6fcf627ab34a"
    }
  },
  {
    "id": "SURVIVAL-PH-01",
    "version": 1,
    "track": "medicine",
    "topic": "Proportional hazards",
    "objective": "Recognise a changing relative effect.",
    "stem": "An intensive care trial shows higher early mortality with treatment, then lower later mortality. A Cox model gives HR 0.98 and scaled Schoenfeld residuals show a strong time trend. Which is the best next step?",
    "options": [
      {
        "id": "A",
        "text": "Examine a time-varying effect and report absolute survival at prespecified times.",
        "feedback": "That represents the changing contrast and gives clinically interpretable comparisons."
      },
      {
        "id": "B",
        "text": "Conclude that survival is equivalent.",
        "feedback": "A near-one summary does not establish equivalence."
      },
      {
        "id": "C",
        "text": "Report 2% lower 90-day mortality risk.",
        "feedback": "A hazard ratio is an instantaneous contrast."
      },
      {
        "id": "D",
        "text": "Ignore the residual plot because Cox regression is nonparametric.",
        "feedback": "The baseline hazard is unspecified, but the covariate ratio still has model assumptions."
      },
      {
        "id": "E",
        "text": "Publish only a log-rank P value.",
        "feedback": "A single test result can hide opposing differences."
      }
    ],
    "correct": "A",
    "hint": "Ask whether a constant relative rate is appropriate.",
    "lesson": "Define the start of follow-up, event and censoring. A right-censored participant contributes observed time at risk. Kaplan\u2013Meier multiplies conditional probabilities of remaining free of the specified event. Death before relapse prevents first relapse and enters its real-world cumulative incidence as a competing event. Log-rank compares curves using observed and expected event counts. A Cox hazard ratio describes relative instantaneous event rates. Its usual constant summary assumes proportional hazards.",
    "explanation": "If the relative effect changes, examine the time pattern and report absolute survival at relevant times. Restricted mean survival time over a prespecified horizon is another interpretable estimand.",
    "operation": "CoxRegression",
    "help": "survival_analysis/cox_regression.htm",
    "lessonId": "survival-competing-risks",
    "sourceIds": [
      "survival-manual"
    ],
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "6e2fd548caf9218464984c11ba8d85e1bb6d4b74cb7d31b7e2ed4c9e6a59d5fb"
    }
  },
  {
    "id": "REGMODEL-OR-01",
    "version": 1,
    "track": "medicine",
    "topic": "Adjusted odds ratio",
    "objective": "Convert an odds ratio to risk at a stated baseline.",
    "stem": "A severity-adjusted logistic model gives OR 0.50 for reattendance with a new respiratory protocol. At the same severity profile, what new-protocol risk corresponds to an old-protocol risk of 50%?",
    "options": [
      {
        "id": "A",
        "text": "25%",
        "feedback": "That halves probability rather than odds."
      },
      {
        "id": "B",
        "text": "33.3%",
        "feedback": "Old odds are 1. New odds are 0.5, giving risk 0.5/(1\u002B0.5)."
      },
      {
        "id": "C",
        "text": "50%",
        "feedback": "An OR of 0.50 changes the odds at this profile."
      },
      {
        "id": "D",
        "text": "66.7%",
        "feedback": "That doubles old odds."
      },
      {
        "id": "E",
        "text": "75%",
        "feedback": "That corresponds to odds of 3."
      }
    ],
    "correct": "B",
    "hint": "Convert baseline risk to odds before applying the ratio.",
    "lesson": "Linear regression models a mean in the outcome\u0027s units, logistic regression models log odds for a binary outcome, and a Poisson model with a log exposure offset models a count rate. Adjust for selected covariates using the design and subject knowledge. Assess functional form, residuals, uncertainty and, for prediction, calibration beyond the development fit.",
    "explanation": "Multiply the old odds 1 by 0.50, then divide the new odds by one plus those odds. The resulting risk is 1/3.",
    "operation": "LogisticRegression",
    "help": "regression_and_correlation/logistic.htm",
    "lessonId": "regression-models",
    "sourceIds": [
      "r-glm",
      "respiratory-sce"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "healthcareBusinessAnalyst"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "bf97fb67b1f61010d477267c7deeaa1b5bef1972e300ebfacc8e37c46908b1e8"
    }
  },
  {
    "id": "REGMODEL-RATE-01",
    "version": 1,
    "track": "analyst",
    "topic": "Poisson offset",
    "objective": "Model event rates with unequal exposure.",
    "stem": "Clinics contribute different person-months and counts of unplanned visits. Which model directly estimates a visit rate ratio for clinic type?",
    "options": [
      {
        "id": "A",
        "text": "Logistic regression on raw visit counts.",
        "feedback": "Visits are counts, and a person can make more than one."
      },
      {
        "id": "B",
        "text": "Linear regression on raw visit counts.",
        "feedback": "Raw counts differ with the exposure time even at the same rate."
      },
      {
        "id": "C",
        "text": "Poisson regression with an unlogged person-time offset.",
        "feedback": "Use the log exposure with the log link."
      },
      {
        "id": "D",
        "text": "Poisson regression with log person-time as an estimated predictor.",
        "feedback": "A freely fitted exposure slope models a different relationship."
      },
      {
        "id": "E",
        "text": "Poisson regression with log person-time as an offset.",
        "feedback": "This models expected count in proportion to exposure and exponentiates the clinic-type coefficient as a rate ratio."
      }
    ],
    "correct": "E",
    "hint": "At equal rates, double the time means double the expected count.",
    "lesson": "Linear regression models a mean in the outcome\u0027s units, logistic regression models log odds for a binary outcome, and a Poisson model with a log exposure offset models a count rate. Adjust for selected covariates using the design and subject knowledge. Assess functional form, residuals, uncertainty and, for prediction, calibration beyond the development fit.",
    "explanation": "With log expected count = log person-time plus the linear predictor, the clinic-type coefficient exponentiates to a rate ratio. Check dispersion and clustering when estimating its uncertainty.",
    "operation": "PoissonRegression",
    "help": "regression_and_correlation/poisson.htm",
    "lessonId": "regression-models",
    "sourceIds": [
      "r-glm",
      "r-family"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "healthcareBusinessAnalyst",
      "publicHealth",
      "postgraduateMedicine"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "7bca8e07baf7690da0cac30a96e99a26f45cb5a36925c650c187004af0833148"
    }
  },
  {
    "id": "REGMODEL-CAL-01",
    "version": 1,
    "track": "researcher",
    "topic": "Calibration",
    "objective": "Distinguish ranking from accurate risk predictions.",
    "stem": "An anaesthesia complications model has AUC 0.85 at a new hospital. Among patients in groups with predicted risks of 20%, 50% and 80%, observed risks are 15%, 40% and 70% with large denominators. Which is the best interpretation?",
    "options": [
      {
        "id": "A",
        "text": "AUC 0.85 establishes accurate risk for 85% of individuals.",
        "feedback": "AUC measures ranking of cases and non-cases."
      },
      {
        "id": "B",
        "text": "The model underpredicts in these groups.",
        "feedback": "Observed risk is lower than predicted in each group."
      },
      {
        "id": "C",
        "text": "The model discriminates fairly well and overpredicts in these groups.",
        "feedback": "Ranking and calibration measure different properties."
      },
      {
        "id": "D",
        "text": "AUC above 0.80 makes calibration unnecessary.",
        "feedback": "A clinical decision threshold needs interpretable probabilities."
      },
      {
        "id": "E",
        "text": "Drop the highest risk group to optimise the estimate.",
        "feedback": "That changes the target population and obscures performance."
      }
    ],
    "correct": "C",
    "hint": "Compare each observed risk with its matched prediction.",
    "lesson": "Linear regression models a mean in the outcome\u0027s units, logistic regression models log odds for a binary outcome, and a Poisson model with a log exposure offset models a count rate. Adjust for selected covariates using the design and subject knowledge. Assess functional form, residuals, uncertainty and, for prediction, calibration beyond the development fit.",
    "explanation": "High discrimination can coexist with overprediction. Assess calibration across the intended range with uncertainty and investigate transportability.",
    "operation": "",
    "help": "regression_and_correlation/logistic.htm",
    "lessonId": "regression-models",
    "sourceIds": [
      "r-glm",
      "frca-research-data"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "mastersStatistics",
      "healthcareBusinessAnalyst"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "e97abf1b707d47a9ba92999c2c022da7cbe31534fe472ee905a8c0ca1be7923c"
    }
  },
  {
    "id": "AGREE-BA-01",
    "version": 1,
    "track": "medicine",
    "topic": "Limits and clinical tolerance",
    "objective": "Compare individual differences with a tolerance.",
    "stem": "A new paediatric monitor correlates 0.99 with the existing one. For new minus existing, mean bias is \u002B2.5 units and estimated limits of agreement are \u002B0.16 to \u002B4.84. Differences above 3 units in either direction matter clinically. Which conclusion is best?",
    "options": [
      {
        "id": "A",
        "text": "Correlation establishes interchangeability.",
        "feedback": "Ranking can agree even when paired differences are large."
      },
      {
        "id": "B",
        "text": "The estimated upper limit exceeds the tolerance. Assess uncertainty before deciding on replacement.",
        "feedback": "The point estimate exceeds \u002B3 and the estimates have sampling uncertainty."
      },
      {
        "id": "C",
        "text": "A mean bias below 3 implies all individual differences are acceptable.",
        "feedback": "Mean bias summarises the average, rather than individual spread."
      },
      {
        "id": "D",
        "text": "The two limits are the confidence interval for mean bias.",
        "feedback": "Limits of agreement describe individual differences."
      },
      {
        "id": "E",
        "text": "Subtract the mean bias and declare all future readings safe.",
        "feedback": "A correction may reduce systematic bias, but residual spread and validation still matter."
      }
    ],
    "correct": "B",
    "hint": "Compare the limit estimates with the prespecified range.",
    "lesson": "Correlation measures association. Two devices can rank patients almost identically and still give differences large enough to change care. For paired quantitative measures, inspect differences against their means and compare the limits of agreement with a prespecified acceptable range. For two categorical raters report the agreement table and observed agreement alongside Cohen\u0027s kappa. For quantitative reliability specify the ICC design, single or average rating, and whether absolute agreement or consistency matters.",
    "explanation": "The limit at \u002B4.84 exceeds \u002B3. Review its uncertainty and the clinical consequences. High correlation and an average difference below 3 are insufficient evidence for replacing the device.",
    "operation": "TPaired",
    "help": "parametric_methods/paired_t.htm",
    "lessonId": "measurement-agreement",
    "sourceIds": [
      "bland-altman",
      "mrcpch-tas"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "postgraduateAlliedHealth"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "d9ed377458cec9366ceff2a3e8e7671101eb74d1efcdb635479b4dbb64a68540"
    }
  },
  {
    "id": "AGREE-KAPPA-01",
    "version": 1,
    "track": "medicine",
    "topic": "Categorical agreement",
    "objective": "Calculate Cohen\u0027s kappa from a table.",
    "stem": "Two emergency clinicians classify 100 fictional records. Both mark 36 positive and both mark 52 negative. Clinician A alone marks 4 positive and clinician B alone marks 8 positive. What is unweighted Cohen\u0027s kappa, to two decimals?",
    "options": [
      {
        "id": "A",
        "text": "0.12",
        "feedback": "This is the observed disagreement fraction."
      },
      {
        "id": "B",
        "text": "0.51",
        "feedback": "This is approximately the marginal expected agreement."
      },
      {
        "id": "C",
        "text": "0.88",
        "feedback": "This is the observed agreement."
      },
      {
        "id": "D",
        "text": "0.37",
        "feedback": "This subtracts expected agreement from observed, but omits the scaling denominator."
      },
      {
        "id": "E",
        "text": "0.75",
        "feedback": "(0.88 \u2212 0.512)/(1 \u2212 0.512) \u2248 0.75."
      }
    ],
    "correct": "E",
    "hint": "Use both positive and negative marginals to calculate expected agreement.",
    "lesson": "Correlation measures association. Two devices can rank patients almost identically and still give differences large enough to change care. For paired quantitative measures, inspect differences against their means and compare the limits of agreement with a prespecified acceptable range. For two categorical raters report the agreement table and observed agreement alongside Cohen\u0027s kappa. For quantitative reliability specify the ICC design, single or average rating, and whether absolute agreement or consistency matters.",
    "explanation": "A\u0027s positive/negative fractions are 0.40/0.60 and B\u0027s 0.44/0.56. Their cross-products for matching categories sum to 0.512. Divide 0.88 \u2212 0.512 by 1 \u2212 0.512.",
    "operation": "KappaScreen",
    "help": "agreement/kappa.htm",
    "lessonId": "measurement-agreement",
    "sourceIds": [
      "cohen-kappa"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "postgraduateAlliedHealth"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "dbc3a7c7159a1cf78a64031d30c0bf7e196dc46e9dd70d4f26e4c1fb0ef30285"
    }
  },
  {
    "id": "AGREE-ICC-01",
    "version": 2,
    "track": "researcher",
    "topic": "ICC specification",
    "objective": "Choose a reliability estimate that matches single-reader use.",
    "stem": "Every image in a reliability study is read by the same two clinicians, viewed as a sample from eligible readers. In practice one reader\u0027s numerical value will be used. Systematic differences between readers matter. Which ICC specification best fits this use?",
    "options": [
      {
        "id": "A",
        "text": "Two-way consistency, average of two readers.",
        "feedback": "Both the definition and number of readings differ from the intended use."
      },
      {
        "id": "B",
        "text": "One-way random-effects ICC for a single reading.",
        "feedback": "The same readers assess all images, allowing their effects to be separated in a two-way design."
      },
      {
        "id": "C",
        "text": "Two-way random-reader absolute agreement, single reading.",
        "feedback": "It addresses generalising to eligible readers, one deployed reading and systematic differences."
      },
      {
        "id": "D",
        "text": "Two-way absolute agreement for the average of two readings.",
        "feedback": "An average often has higher reliability than a single reading, but a single reading is used here."
      },
      {
        "id": "E",
        "text": "Pearson correlation of the two readers\u0027 scores.",
        "feedback": "Correlation does not penalise a systematic shift."
      }
    ],
    "correct": "C",
    "hint": "Ask who reads each image, how many readings are used and whether absolute values matter.",
    "lesson": "Correlation measures association. Two devices can rank patients almost identically and still give differences large enough to change care. For paired quantitative measures, inspect differences against their means and compare the limits of agreement with a prespecified acceptable range. For two categorical raters report the agreement table and observed agreement alongside Cohen\u0027s kappa. For quantitative reliability specify the ICC design, single or average rating, and whether absolute agreement or consistency matters.",
    "explanation": "Use two-way random-reader absolute agreement for a single measurement under the stated design and assumptions. Report its form and interval alongside the clinical consequences of measurement differences.",
    "operation": "Agreement",
    "help": "agreement/intraclass.htm",
    "lessonId": "measurement-agreement",
    "sourceIds": [
      "icc-selection",
      "bland-altman"
    ],
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "postgraduateAlliedHealth",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "128e1ffa75fda946cd31e0290319da08946dda2e418da84e848e6ebbdb45e7d4"
    }
  },
  {
    "id": "SPC-LIMIT-01",
    "version": 2,
    "track": "analyst",
    "topic": "p-chart limits",
    "objective": "Interpret equal proportions with unequal denominators.",
    "stem": "An emergency department\u0027s stable baseline is 8% delayed reviews. One later month has 15 delays among 100 eligible encounters, another has 45 among 300. Using a simple 3 SD binomial p-chart with baseline fixed, which statement is correct?",
    "options": [
      {
        "id": "A",
        "text": "Both breach because both exceed 8%.",
        "feedback": "The centre line is not a control limit."
      },
      {
        "id": "B",
        "text": "Both must give the same signal because both equal 15%.",
        "feedback": "The denominator determines the width of the band."
      },
      {
        "id": "C",
        "text": "The smaller month breaches first because its estimate is noisier.",
        "feedback": "Expected variation is greater at smaller n, so the limit is wider."
      },
      {
        "id": "D",
        "text": "Only the 300-encounter month breaches the upper limit.",
        "feedback": "The two limits are about 16.1% and 12.7%."
      },
      {
        "id": "E",
        "text": "No control limit can be calculated without a clinical target.",
        "feedback": "The limits use the baseline proportion and denominator."
      }
    ],
    "correct": "D",
    "hint": "Calculate 0.08 \u002B 3\u221A(0.08\u00D70.92/n) in each month.",
    "lesson": "An audit defines a standard, eligibility and a denominator, checks current care, acts on findings and remeasures. Quality improvement measures are followed in time order with a stable definition. A control chart estimates expected variation in a stable process. A p-chart models the proportion of eligible encounters with a binary outcome and its limits vary with denominator. Clinical targets, control limits and confidence intervals serve different purposes.",
    "explanation": "At n=100 the limit is 16.1%, so 15% is within it. At n=300 it is 12.7%, so 15% breaches it. Verify the observation and investigate a potential special cause.",
    "operation": "",
    "help": "graphics/control.htm",
    "lessonId": "audit-spc",
    "sourceIds": [
      "nist-p-chart",
      "nhs-spc"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "healthcareBusinessAnalyst",
      "publicHealth"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "4a798ef816e08f89db9bb279143d69afdfd433dfc8d2a10f1ec39a97f7e649fe"
    }
  },
  {
    "id": "SPC-RESPONSE-01",
    "version": 2,
    "track": "medicine",
    "topic": "Responding to common causes",
    "objective": "Choose a response to stable performance short of a standard.",
    "stem": "For a year an ICU process has remained within its established control limits, with no chart-rule signals. The service\u2019s target maximum adverse-event rate is below every observed rate. Which action fits this evidence?",
    "options": [
      {
        "id": "A",
        "text": "Study and redesign the underlying process, then continue monitoring.",
        "feedback": "Stable process variation can coexist with performance below the standard."
      },
      {
        "id": "B",
        "text": "Investigate each fluctuation as a separate staff error.",
        "feedback": "Ordinary variation need not have a separate assignable cause in each month."
      },
      {
        "id": "C",
        "text": "Raise the target to the current centre and declare success.",
        "feedback": "The service target and process centre answer different questions."
      },
      {
        "id": "D",
        "text": "Use the latest value as evidence of a sustained improvement.",
        "feedback": "A single point within the observed pattern does not show a sustained change."
      },
      {
        "id": "E",
        "text": "Stop measuring because the process is statistically stable.",
        "feedback": "Stability says little about whether care meets a clinical standard."
      }
    ],
    "correct": "A",
    "hint": "Decide separately whether the process is stable and whether it meets the standard.",
    "lesson": "An audit defines a standard, eligibility and a denominator, checks current care, acts on findings and remeasures. Quality improvement measures are followed in time order with a stable definition. A control chart estimates expected variation in a stable process. A p-chart models the proportion of eligible encounters with a binary outcome and its limits vary with denominator. Clinical targets, control limits and confidence intervals serve different purposes.",
    "explanation": "A stable process can consistently perform poorly. Examine its underlying design, monitor for change after intervention and keep the clinical target distinct from expected process variation.",
    "operation": "",
    "help": "graphics/control.htm",
    "lessonId": "audit-spc",
    "sourceIds": [
      "nhs-spc",
      "frcem-research-qi"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "healthcareBusinessAnalyst"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "6809cabb79a255bcc3b40985458dfc7e6bea360c1594ab07655807c52930a244"
    }
  },
  {
    "id": "SPC-STANDARD-01",
    "version": 1,
    "track": "publicHealth",
    "topic": "Rates and case mix",
    "objective": "Use common weights to compare sites.",
    "stem": "For an illustrative site comparison, low/high-frailty rates per 1,000 patient-days are 1 and 6 at A, and 2 and 8 at B. Crude rates are 4.75 at A and 3.2 at B. With a common 50:50 person-time mix, what are the directly standardised rates?",
    "options": [
      {
        "id": "A",
        "text": "A 4.75, B 3.2.",
        "feedback": "Those are the crude rates with each site\u0027s own mix."
      },
      {
        "id": "B",
        "text": "A 3.5, B 5.0.",
        "feedback": "Apply the same half weight to each stratum in each site."
      },
      {
        "id": "C",
        "text": "A 7, B 10.",
        "feedback": "Add the two stratum rates only after weighting them."
      },
      {
        "id": "D",
        "text": "A 5.0, B 3.5.",
        "feedback": "The weighted rates have been assigned to the opposite sites."
      },
      {
        "id": "E",
        "text": "A 1, B 2.",
        "feedback": "That compares the low-frailty stratum alone."
      }
    ],
    "correct": "B",
    "hint": "Apply the same weights to both sets of stratum rates.",
    "lesson": "An audit defines a standard, eligibility and a denominator, checks current care, acts on findings and remeasures. Quality improvement measures are followed in time order with a stable definition. A control chart estimates expected variation in a stable process. A p-chart models the proportion of eligible encounters with a binary outcome and its limits vary with denominator. Clinical targets, control limits and confidence intervals serve different purposes.",
    "explanation": "A is 0.5\u00D71 \u002B 0.5\u00D76 = 3.5 and B is 0.5\u00D72 \u002B 0.5\u00D78 = 5.0. The common mix reverses the crude comparison. Case-mix adjustment does not settle measurement comparability or uncertainty.",
    "operation": "RateDirect",
    "help": "rates/direct_standardization.htm",
    "lessonId": "audit-spc",
    "sourceIds": [
      "nhs-direct-standardisation"
    ],
    "difficulty": "intermediate",
    "learnerGoals": [
      "publicHealth",
      "healthcareBusinessAnalyst",
      "postgraduateMedicine"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "ec4e23ff16a926d32c8460260dd2a5d6d0faf92fb79d9084a207bd9b7c0ba7a7"
    }
  },
  {
    "id": "BIO-ABSTRACT-01",
    "version": 2,
    "track": "core",
    "topic": "Clinical evidence",
    "objective": "Calculate an absolute benefit.",
    "stem": "A 90-day trial reports that 30 of 500 people allocated to a new treatment and 50 of 500 allocated to usual care had at least one admission. How many fewer people per 1,000 allocated to the new treatment had at least one admission by day 90, compared with usual care?",
    "options": [
      {
        "id": "A",
        "text": "20",
        "feedback": "Twenty is the raw difference between groups of 500."
      },
      {
        "id": "B",
        "text": "40",
        "feedback": "The absolute difference of 0.04 corresponds to 40 fewer people with at least one admission per 1,000."
      },
      {
        "id": "C",
        "text": "60",
        "feedback": "Sixty per 1,000 is the treatment risk."
      },
      {
        "id": "D",
        "text": "100",
        "feedback": "One hundred per 1,000 is the usual care risk."
      },
      {
        "id": "E",
        "text": "400",
        "feedback": "40% is the relative reduction in the risk of having at least one admission. The question asks for the absolute difference per 1,000 people."
      }
    ],
    "correct": "B",
    "hint": "Calculate the two risks before subtracting.",
    "lesson": "Report a common denominator and time.",
    "explanation": "The absolute difference is 50/500 \u2212 30/500 = 0.04. This estimates 40 fewer people with at least one admission per 1,000 allocated to the new treatment by day 90.",
    "help": "clinical_epidemiology/nnt.htm",
    "operation": "",
    "lessonId": "clinical-abstract",
    "difficulty": "beginner",
    "learnerGoals": [
      "undergraduateMedicine",
      "postgraduateMedicine",
      "publicHealth",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "usmle-2026"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "bc0c3d6775c62558431e24dd32964c9ec969212766e3099f07393929377cb143"
    }
  },
  {
    "id": "BIO-ABSTRACT-02",
    "version": 1,
    "track": "core",
    "topic": "Clinical evidence",
    "objective": "Calculate the number needed to harm.",
    "stem": "Serious rash occurs in 15 of 500 treated patients and 5 of 500 usual-care patients by 90 days. What is the estimated number needed to harm for serious rash?",
    "options": [
      {
        "id": "A",
        "text": "10",
        "feedback": "Ten is the raw excess count, not a number needed to harm."
      },
      {
        "id": "B",
        "text": "20",
        "feedback": "Twenty is the excess per 1,000 patients."
      },
      {
        "id": "C",
        "text": "33",
        "feedback": "This roughly inverts the treated risk alone."
      },
      {
        "id": "D",
        "text": "50",
        "feedback": "Take the reciprocal of the 0.02 risk increase."
      },
      {
        "id": "E",
        "text": "100",
        "feedback": "This inverts the usual-care risk alone."
      }
    ],
    "correct": "D",
    "hint": "Subtract the risks, not the raw counts.",
    "lesson": "The number needed to harm refers to a particular outcome over a particular time.",
    "explanation": "The extra risk is 15/500 \u2212 5/500 = 0.02. Its reciprocal is 50 for one additional serious rash by 90 days.",
    "help": "clinical_epidemiology/nnt.htm",
    "operation": "",
    "lessonId": "clinical-abstract",
    "difficulty": "beginner",
    "learnerGoals": [
      "undergraduateMedicine",
      "postgraduateMedicine",
      "publicHealth",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "usmle-2026"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "54d414a928974f924473a72e79b49db569e9a23915a5cb8b29c64b6e989c1103"
    }
  },
  {
    "id": "BIO-ABSTRACT-03",
    "version": 2,
    "track": "core",
    "topic": "Clinical evidence",
    "objective": "Distinguish absolute and relative effects across settings.",
    "stem": "Assume a treatment\u0027s 90-day admission risk ratio of 0.60 also applies in a clinic where untreated risk is 2.5%. What absolute benefit would this model predict?",
    "options": [
      {
        "id": "A",
        "text": "1.0 percentage point",
        "feedback": "Treated risk would be 0.60 \u00D7 2.5% = 1.5%."
      },
      {
        "id": "B",
        "text": "1.5 percentage points",
        "feedback": "This is the predicted treated risk expressed as a percentage, not the reduction."
      },
      {
        "id": "C",
        "text": "2.5 percentage points",
        "feedback": "This is the untreated risk."
      },
      {
        "id": "D",
        "text": "4.0 percentage points",
        "feedback": "A 4.0-point reduction would require a 10% untreated risk at this risk ratio. The untreated risk here is 2.5%."
      },
      {
        "id": "E",
        "text": "40 percentage points",
        "feedback": "This confuses a 40% relative reduction with an absolute difference."
      }
    ],
    "correct": "A",
    "hint": "What treated risk does the stated ratio imply?",
    "lesson": "Absolute benefit depends on baseline risk if relative benefit is held constant.",
    "explanation": "Under the stated assumption, the predicted treated risk is 1.5% and the absolute reduction is 1.0 percentage point. Transporting the risk ratio is itself an assumption.",
    "help": "clinical_epidemiology/nnt.htm",
    "operation": "",
    "lessonId": "clinical-abstract",
    "difficulty": "beginner",
    "learnerGoals": [
      "undergraduateMedicine",
      "postgraduateMedicine",
      "publicHealth",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "usmle-2026"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "cdb8deb03d442679edda5525e4e1297ceb19ac712a339b937816498b892a927a"
    }
  },
  {
    "id": "BIO-CAUSAL-01",
    "version": 1,
    "track": "researcher",
    "topic": "Causal inference",
    "objective": "Recognise interaction on the absolute scale.",
    "stem": "A treatment halves event risk in both age strata. Untreated risks are 10% in younger people and 30% in older people. How do absolute benefits compare?",
    "options": [
      {
        "id": "A",
        "text": "They are each 50 percentage points.",
        "feedback": "The ratio halves the stated baseline risk; it does not subtract 50 points."
      },
      {
        "id": "B",
        "text": "They must be identical.",
        "feedback": "An identical ratio need not produce an identical risk difference."
      },
      {
        "id": "C",
        "text": "5 points younger, 15 points older.",
        "feedback": "Halving the two baseline risks prevents different numbers of events per 100."
      },
      {
        "id": "D",
        "text": "15 points younger, 5 points older.",
        "feedback": "The higher baseline risk has the larger absolute benefit here."
      },
      {
        "id": "E",
        "text": "They cannot be calculated.",
        "feedback": "The baseline risks and the stratum-specific ratios suffice for these contrasts."
      }
    ],
    "correct": "C",
    "hint": "Halve each untreated risk and subtract.",
    "lesson": "Effect modification depends on the scale.",
    "explanation": "Risk moves from 10% to 5%, and from 30% to 15%. The absolute reduction is larger in the older group.",
    "help": "basics/confounding.htm",
    "operation": "",
    "lessonId": "causal-adjustment",
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "what-if"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "7328ba85ac120eedb7debc0ee42d8b062be8352d6b23080b0bfbc3cf8a52773d"
    }
  },
  {
    "id": "BIO-CAUSAL-02",
    "version": 1,
    "track": "researcher",
    "topic": "Causal inference",
    "objective": "Distinguish confounders and mediators.",
    "stem": "Baseline illness severity affects both treatment uptake and later admission. Treatment reduces symptoms at one week, and that change affects admission. Which adjustment set is most appropriate for the total treatment effect, assuming no other common causes and this causal structure?",
    "options": [
      {
        "id": "A",
        "text": "One-week symptoms only.",
        "feedback": "Symptoms are downstream of the treatment and lie on a pathway to the outcome."
      },
      {
        "id": "B",
        "text": "Neither variable.",
        "feedback": "Baseline severity opens a noncausal association between treatment and admission."
      },
      {
        "id": "C",
        "text": "Both variables.",
        "feedback": "Adjusting for symptoms can block part of the total effect."
      },
      {
        "id": "D",
        "text": "Baseline illness severity only.",
        "feedback": "This addresses the stated confounding without removing the effect through symptoms."
      },
      {
        "id": "E",
        "text": "Only people who improve by one week.",
        "feedback": "Selecting on a post-treatment variable can bias the comparison."
      }
    ],
    "correct": "D",
    "hint": "Order the variables in time and ask which precedes treatment.",
    "lesson": "Choose controls for the causal effect of interest.",
    "explanation": "Baseline severity is a common cause; the subsequent symptoms are a mediator in the stated structure. Adjustment for the mediator does not in general estimate the total effect.",
    "help": "basics/confounding.htm",
    "operation": "",
    "lessonId": "causal-adjustment",
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "what-if"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "6bd1119a65760f90aa6b480248bf018acbb968a0367ce6e528c3242e41a05496"
    }
  },
  {
    "id": "BIO-CAUSAL-03",
    "version": 1,
    "track": "researcher",
    "topic": "Causal inference",
    "objective": "Identify a threat to comparing treatment strategies.",
    "stem": "In one patient subgroup, nobody receives the new treatment. What limits an adjusted comparison for that subgroup using only this cohort?",
    "options": [
      {
        "id": "A",
        "text": "There is no treatment overlap in that subgroup.",
        "feedback": "A data-based comparison needs observations under both strategies within the subgroup."
      },
      {
        "id": "B",
        "text": "The problem disappears by choosing logistic regression.",
        "feedback": "A model can extrapolate, but this is not evidence under the unobserved strategy."
      },
      {
        "id": "C",
        "text": "A larger sample with the same treatment rule guarantees overlap.",
        "feedback": "A rule that excludes treatment in this subgroup can persist at any sample size."
      },
      {
        "id": "D",
        "text": "Only the confidence level needs changing.",
        "feedback": "Confidence levels do not supply missing treatment comparisons."
      },
      {
        "id": "E",
        "text": "The subgroup proves the treatment is ineffective.",
        "feedback": "It provides no observed treated outcome."
      }
    ],
    "correct": "A",
    "hint": "Can we observe both strategies for these patients?",
    "lesson": "Positivity is a condition for identification from observed comparisons.",
    "explanation": "The data lack overlap in this subgroup. Change the question to a supported population or state and justify external information or extrapolation.",
    "help": "basics/confounding.htm",
    "operation": "",
    "lessonId": "causal-adjustment",
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "what-if"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "b3878510063a0177c8a80807f927e435a8a9c31fa6e180d2bca6a06ff8a85f46"
    }
  },
  {
    "id": "BIO-SYNTH-01",
    "version": 1,
    "track": "researcher",
    "topic": "Evidence synthesis",
    "objective": "Distinguish the interval for a mean effect from an interval for a new setting.",
    "stem": "A meta-analysis estimates a 2-day average reduction in recovery time. Its 95% confidence interval for the mean excludes zero, but its prediction interval includes no reduction and some prolongation. Which interpretation is best?",
    "options": [
      {
        "id": "A",
        "text": "Every setting should expect a shorter recovery.",
        "feedback": "A confidence interval for a mean does not describe every setting."
      },
      {
        "id": "B",
        "text": "The study outcomes must have been measured incorrectly.",
        "feedback": "The pattern can arise from real heterogeneity."
      },
      {
        "id": "C",
        "text": "The two results contradict each other.",
        "feedback": "They are intervals for different quantities."
      },
      {
        "id": "D",
        "text": "The result rules out increased recovery time in a new setting.",
        "feedback": "The prediction interval allows such an effect."
      },
      {
        "id": "E",
        "text": "The estimated average favours the intervention, but effects in comparable settings may differ.",
        "feedback": "The distinction matters when choosing whether to adopt the intervention."
      }
    ],
    "correct": "E",
    "hint": "Ask what each interval is intended to cover.",
    "lesson": "A pooled effect is a summary of study evidence, not a guaranteed local effect.",
    "explanation": "The mean-effect interval reflects uncertainty in the average. A prediction interval accounts for heterogeneity and allows no benefit or harm in a comparable setting under the model.",
    "help": "meta_analysis/summary.htm",
    "operation": "",
    "lessonId": "evidence-synthesis",
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "cochrane-meta"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "836616654efd6ebae9b15ae66a8ae1cdab93adf73fdd2e7f494fd6d3c779e48f"
    }
  },
  {
    "id": "BIO-SYNTH-02",
    "version": 2,
    "track": "researcher",
    "topic": "Evidence synthesis",
    "objective": "Interpret funnel plot asymmetry.",
    "stem": "Small studies in a systematic review report larger benefits than larger studies. What can be concluded from this finding alone?",
    "options": [
      {
        "id": "A",
        "text": "Selective publication is proven.",
        "feedback": "It is one possible explanation, not proven by the pattern."
      },
      {
        "id": "B",
        "text": "Several explanations are possible, including selective reporting and genuine differences in populations.",
        "feedback": "The pattern prompts checks of study methods, setting and missing results."
      },
      {
        "id": "C",
        "text": "The pooled effect is automatically unbiased with random effects.",
        "feedback": "Random effects do not correct biased studies or absent results."
      },
      {
        "id": "D",
        "text": "All small studies must be excluded in every review.",
        "feedback": "Exclusion needs a principled, preferably prespecified reason."
      },
      {
        "id": "E",
        "text": "The largest study is definitely correct.",
        "feedback": "Size does not guarantee good design or applicability."
      }
    ],
    "correct": "B",
    "hint": "Distinguish a signal from its cause.",
    "lesson": "Different results in smaller studies call for investigation.",
    "explanation": "Larger benefits reported in smaller studies have several possible explanations. Consider chance, genuine differences in populations or methods, and selective reporting or publication.",
    "help": "meta_analysis/summary.htm",
    "operation": "",
    "lessonId": "evidence-synthesis",
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "cochrane-meta"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "5d820e45c513181e410b4c7b1148d6b78b072b2b34b6d947c0feaabdeb7f2f0f"
    }
  },
  {
    "id": "BIO-SYNTH-03",
    "version": 1,
    "track": "researcher",
    "topic": "Evidence synthesis",
    "objective": "Assess the certainty of an effect estimate in a target population.",
    "stem": "A review finds a precise benefit in young adults. The decision concerns frail older people with multiple conditions who were excluded from all studies. Which aspect is specifically threatened by this mismatch?",
    "options": [
      {
        "id": "A",
        "text": "Indirectness",
        "feedback": "The included population differs from those to whom the result would apply."
      },
      {
        "id": "B",
        "text": "Sampling imprecision in the pooled estimate",
        "feedback": "The interval can be narrow even when populations differ."
      },
      {
        "id": "C",
        "text": "Inter-rater reliability",
        "feedback": "There is no stated measurement disagreement."
      },
      {
        "id": "D",
        "text": "Publication bias",
        "feedback": "Differences in participants do not establish suppressed results."
      },
      {
        "id": "E",
        "text": "Allocation concealment",
        "feedback": "How assignment was hidden is a separate study-level concern."
      }
    ],
    "correct": "A",
    "hint": "Ask who was studied and who will receive treatment.",
    "lesson": "Certainty depends partly on how directly the studies answer your question.",
    "explanation": "Even a precise estimate can be indirect for a different population. Assess the clinical reasons that the benefit or harm might change.",
    "help": "meta_analysis/summary.htm",
    "operation": "",
    "lessonId": "evidence-synthesis",
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "cochrane-grade"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "d4351ed9fedca665aed1a9e1ca8b0a7c69fef2a20c3a7ef89b98a04023b3b064"
    }
  },
  {
    "id": "BIO-IMPACT-01",
    "version": 1,
    "track": "publicHealth",
    "topic": "Population health",
    "objective": "Calculate a population attributable fraction from a stated scenario.",
    "stem": "A population has 300 annual cases. A defined intervention is expected to prevent 50 of them without other changes. What proportion of the current population cases would it prevent?",
    "options": [
      {
        "id": "A",
        "text": "1%",
        "feedback": "This might be a change in population risk if population size were known, not the proportion of current cases."
      },
      {
        "id": "B",
        "text": "6%",
        "feedback": "This is not the ratio of prevented to observed cases."
      },
      {
        "id": "C",
        "text": "16.7%",
        "feedback": "Fifty divided by 300 is one sixth."
      },
      {
        "id": "D",
        "text": "50%",
        "feedback": "The numerator is a count of 50 cases."
      },
      {
        "id": "E",
        "text": "83.3%",
        "feedback": "This is the fraction of cases expected to remain."
      }
    ],
    "correct": "C",
    "hint": "Use the total number of current cases as the denominator.",
    "lesson": "Keep population risk and fraction of cases distinct.",
    "explanation": "Under the assumptions the intervention would avert 50/300, or about 16.7% of current cases.",
    "help": "rates/direct_standardization.htm",
    "operation": "",
    "lessonId": "population-impact",
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "cdc-field"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "2b4f7010c06dc9bc8037ede9ac1a9edd950ad80c229ac427e9d0e7a4e58ba07f"
    }
  },
  {
    "id": "BIO-IMPACT-02",
    "version": 2,
    "track": "publicHealth",
    "topic": "Population health",
    "objective": "Translate a causal risk ratio into relative vaccine effectiveness.",
    "stem": "With equal ascertainment and adequate follow-up, a fictional randomised vaccine trial estimates a risk ratio of 0.25 for symptomatic infection, vaccine versus placebo. What is vaccine efficacy on the relative risk-reduction scale?",
    "options": [
      {
        "id": "A",
        "text": "4%",
        "feedback": "The reciprocal risk ratio is 1/0.25 = 4. Vaccine efficacy instead uses (1 \u2212 0.25) \u00D7 100% = 75%."
      },
      {
        "id": "B",
        "text": "25%",
        "feedback": "This is the vaccinated group\u0027s risk relative to placebo."
      },
      {
        "id": "C",
        "text": "50%",
        "feedback": "The reduction is not half here."
      },
      {
        "id": "D",
        "text": "75%",
        "feedback": "Relative risk reduction is one minus the risk ratio."
      },
      {
        "id": "E",
        "text": "100%",
        "feedback": "Some symptomatic infections remain in the vaccinated group."
      }
    ],
    "correct": "D",
    "hint": "Compare the ratio with a ratio of one.",
    "lesson": "Name the endpoint and duration when reporting efficacy.",
    "explanation": "The estimated efficacy against symptomatic infection is 1 \u2212 0.25 = 75% over the trial follow-up. This does not imply 75 fewer cases per 100 vaccinated.",
    "help": "rates/direct_standardization.htm",
    "operation": "",
    "lessonId": "population-impact",
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "usmle-2026",
      "cdc-field"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "434778b219775b78ca61cd279495046e228d4ac06938f292d9eee90924220228"
    }
  },
  {
    "id": "BIO-IMPACT-03",
    "version": 2,
    "track": "publicHealth",
    "topic": "Population health",
    "objective": "Calculate an incremental cost per QALY.",
    "stem": "For a fictional programme the expected extra total cost is 50,000 currency units and the expected extra health gain is 2 QALYs against current care. What is the incremental cost per QALY?",
    "options": [
      {
        "id": "A",
        "text": "25,000",
        "feedback": "Divide incremental costs by incremental QALYs."
      },
      {
        "id": "B",
        "text": "50,000",
        "feedback": "This is the total incremental cost."
      },
      {
        "id": "C",
        "text": "100,000",
        "feedback": "100,000 comes from multiplying 50,000 by 2. Incremental cost per QALY divides the extra cost by the extra QALYs."
      },
      {
        "id": "D",
        "text": "2",
        "feedback": "This is the incremental health gain."
      },
      {
        "id": "E",
        "text": "It cannot be computed without the total cost of each plan.",
        "feedback": "The differences suffice for this arithmetic."
      }
    ],
    "correct": "A",
    "hint": "What additional cost corresponds to one additional QALY?",
    "lesson": "An incremental ratio compares two stated strategies.",
    "explanation": "50,000 / 2 = 25,000 currency units per additional QALY. Whether the programme represents value requires evidence and a decision framework.",
    "help": "rates/direct_standardization.htm",
    "operation": "",
    "lessonId": "population-impact",
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "nice-economic"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "a28badd70edc0b71f0d06f5e3103434c1baf739caaf64cd5734e1f5cf3d68e56"
    }
  },
  {
    "id": "BIO-UNCERT-01",
    "version": 1,
    "track": "researcher",
    "topic": "Statistical inference",
    "objective": "Calculate the familywise error probability under independent true nulls.",
    "stem": "Twenty independent tests all have true null hypotheses. Each rejects falsely with probability 0.05. What is the probability of at least one false rejection?",
    "options": [
      {
        "id": "A",
        "text": "5%",
        "feedback": "This is the probability for a single test."
      },
      {
        "id": "B",
        "text": "36%",
        "feedback": "This is close to the probability of no false rejections."
      },
      {
        "id": "C",
        "text": "50%",
        "feedback": "The probability should be calculated from the complement."
      },
      {
        "id": "D",
        "text": "64%",
        "feedback": "The complement of 0.95 to the 20th power is about 0.642."
      },
      {
        "id": "E",
        "text": "100%",
        "feedback": "The expected number of false rejections is one; that does not mean at least one is certain."
      }
    ],
    "correct": "D",
    "hint": "Calculate the probability all 20 avoid a false rejection.",
    "lesson": "A familywise error rate concerns at least one error among a set of tests.",
    "explanation": "Under independence the probability is 1 \u2212 0.95^20 \u2248 64.2%. Under dependence this formula generally changes.",
    "help": "basics/missing_data.htm",
    "operation": "",
    "lessonId": "uncertainty-sensitivity",
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "usmle-2026"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "654abbc8e31a7aef5ae829cc467be418a2f28f75d2508f3069109118fdd21703"
    }
  },
  {
    "id": "BIO-UNCERT-02",
    "version": 2,
    "track": "researcher",
    "topic": "Statistical inference",
    "objective": "Calculate a worst-case risk difference with missing binary outcomes.",
    "stem": "In a fictional trial with 100 patients per arm, 10 outcomes in each arm are unknown. Known event totals are 8 in treatment and 12 in control. What is the largest possible treatment-minus-control risk difference?",
    "options": [
      {
        "id": "A",
        "text": "\u221214 percentage points",
        "feedback": "This is the smallest possible difference."
      },
      {
        "id": "B",
        "text": "\u22124 percentage points",
        "feedback": "Equal numbers of events among missing participants in the two arms would leave the difference at \u22124 points."
      },
      {
        "id": "C",
        "text": "0 percentage points",
        "feedback": "The extremes allow an excess in treatment."
      },
      {
        "id": "D",
        "text": "\u002B4 percentage points",
        "feedback": "\u002B4 reverses the difference for the known events. To maximise treatment minus control, assign an event to all ten missing treatment participants and to none of the missing controls. That gives \u002B6 points."
      },
      {
        "id": "E",
        "text": "\u002B6 percentage points",
        "feedback": "The extreme is (8 \u002B 10)/100 \u2212 12/100."
      }
    ],
    "correct": "E",
    "hint": "Make treatment risk as high and control risk as low as possible.",
    "lesson": "A worst-case bound does not require missing outcomes to be random.",
    "explanation": "The maximum is 18% \u2212 12% = 6 percentage points. This ignores sampling uncertainty: it is a bound for the observed trial cohort.",
    "help": "basics/missing_data.htm",
    "operation": "",
    "lessonId": "uncertainty-sensitivity",
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "cochrane-missing"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "6ba8692b80b003b9b72fc8b7b50f8dc5941132866348ec8fbe7296690866b5f2"
    }
  },
  {
    "id": "BIO-UNCERT-03",
    "version": 1,
    "track": "researcher",
    "topic": "Statistical inference",
    "objective": "Recognise missing not at random.",
    "stem": "Even after accounting for recorded baseline severity, patients with higher unrecorded outcome symptom scores are less likely to respond. Which missingness description fits the stated mechanism?",
    "options": [
      {
        "id": "A",
        "text": "Missing not at random",
        "feedback": "Response still depends on the unobserved outcome after conditioning on measured variables."
      },
      {
        "id": "B",
        "text": "Missing completely at random",
        "feedback": "Response is not independent of the data."
      },
      {
        "id": "C",
        "text": "Missing at random given baseline severity",
        "feedback": "The stem specifies dependence on the unobserved outcome even at the same severity."
      },
      {
        "id": "D",
        "text": "No missing-data problem after complete-case analysis",
        "feedback": "Dropping missing outcomes does not remove selection on the outcome."
      },
      {
        "id": "E",
        "text": "Randomisation guarantees unbiased complete-case comparison",
        "feedback": "Allocation does not guarantee that observed outcomes are a representative subset."
      }
    ],
    "correct": "A",
    "hint": "What information predicts response after conditioning on measured variables?",
    "lesson": "The missingness mechanism is an assumption, not determined by the observed data alone.",
    "explanation": "The hypothetical mechanism depends on the missing outcome itself after accounting for observed covariates, so it is MNAR.",
    "help": "basics/missing_data.htm",
    "operation": "",
    "lessonId": "uncertainty-sensitivity",
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "rcpsych-b",
      "cochrane-missing"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "3e7342e764ef21e0ca621b081ec95e054fb36e87187e3ec3daa64d2aacd692ec"
    }
  },
  {
    "id": "BIO-DESIGN-01",
    "version": 1,
    "track": "core",
    "topic": "Study design",
    "objective": "Identify controls from the population that produced the cases.",
    "stem": "A clinic studies whether an exposure is associated with an acute condition using a case-control design. Cases are residents of its catchment who developed the condition during one year. Which criterion matters most for selecting controls?",
    "options": [
      {
        "id": "A",
        "text": "They had no exposure of interest.",
        "feedback": "Selecting on exposure can distort the comparison."
      },
      {
        "id": "B",
        "text": "Their age exactly equals the mean age of the cases.",
        "feedback": "Source population membership is central. Matching is optional and does not mean selecting the case-group mean."
      },
      {
        "id": "C",
        "text": "They come from people who could have been identified as cases had they developed the condition that year.",
        "feedback": "Controls should reflect the relevant population and time at risk."
      },
      {
        "id": "D",
        "text": "They are from a different region with no access to the clinic.",
        "feedback": "The region may have a different exposure distribution and different case ascertainment."
      },
      {
        "id": "E",
        "text": "Their number equals the number of cases.",
        "feedback": "The number of controls concerns precision, not the fundamental selection criterion."
      }
    ],
    "correct": "C",
    "hint": "Ask where the cases came from before matching or counting controls.",
    "lesson": "Controls provide a comparison of exposure in a specified source population.",
    "explanation": "Controls should come from the source population that generated the cases under the study\u0027s eligibility and ascertainment rules.",
    "help": "basics/bias.htm",
    "operation": "",
    "lessonId": "study-designs",
    "difficulty": "intermediate",
    "learnerGoals": [
      "undergraduateMedicine",
      "postgraduateMedicine",
      "publicHealth",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "cdc-design"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "527e9c8c6fb58b50a98de33736955ea544bd56bd7627960f867341f52ea9f6f3"
    }
  },
  {
    "id": "BIO-DESIGN-02",
    "version": 1,
    "track": "core",
    "topic": "Study design",
    "objective": "Interpret an odds ratio without mistaking a case-control sample for a cohort.",
    "stem": "A case-control study observes 40 exposed and 20 unexposed cases, and 20 exposed and 40 unexposed controls. Which quantity can be calculated directly as a population association estimate under appropriate sampling?",
    "options": [
      {
        "id": "A",
        "text": "Population disease prevalence of 50%",
        "feedback": "The number of cases sampled was chosen by design."
      },
      {
        "id": "B",
        "text": "Population risk difference of 33 percentage points",
        "feedback": "There are no population disease-risk denominators here."
      },
      {
        "id": "C",
        "text": "Risk ratio of 2",
        "feedback": "The disease risks cannot be obtained from the selected counts."
      },
      {
        "id": "D",
        "text": "Exposure odds ratio of 4",
        "feedback": "Divide 40/20 by 20/40."
      },
      {
        "id": "E",
        "text": "Incidence rate of 60 per 120 person-years",
        "feedback": "The denominators are sampled people and do not measure person-time."
      }
    ],
    "correct": "D",
    "hint": "Compare the odds of exposure in cases with those in controls.",
    "lesson": "Use effect measures that are identified by the sampling design.",
    "explanation": "The odds ratio is (40 \u00D7 40)/(20 \u00D7 20) = 4. It is not automatically a risk ratio.",
    "help": "basics/bias.htm",
    "operation": "",
    "lessonId": "study-designs",
    "difficulty": "intermediate",
    "learnerGoals": [
      "undergraduateMedicine",
      "postgraduateMedicine",
      "publicHealth",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "cdc-design"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "aba55b04da4ec88d4f42897fa8205bba797a6fe851d9f63844bf514cbd60c7c0"
    }
  },
  {
    "id": "BIO-DESIGN-03",
    "version": 1,
    "track": "core",
    "topic": "Study design",
    "objective": "Identify a suitable qualitative contribution to a mixed-methods study.",
    "stem": "Attendance records show low uptake of a service in certain locations. The team wants to understand why before changing delivery. Which next step best adds qualitative evidence?",
    "options": [
      {
        "id": "A",
        "text": "Interview diverse eligible people and staff, examine recurring themes and integrate them with the uptake patterns.",
        "feedback": "This investigates reasons and adds to the quantitative description."
      },
      {
        "id": "B",
        "text": "Treat non-attendance as proof of one common motive.",
        "feedback": "Records show an action, not the reasons for it."
      },
      {
        "id": "C",
        "text": "Report each theme as its prevalence in the entire catchment.",
        "feedback": "A purposive qualitative sample does not usually support that inference."
      },
      {
        "id": "D",
        "text": "Choose the most favourable explanation from an uptake chart.",
        "feedback": "The chart cannot establish participants\u0027 experiences."
      },
      {
        "id": "E",
        "text": "Increase the significance threshold until the data explain the reasons.",
        "feedback": "A threshold change does not supply evidence about motives."
      }
    ],
    "correct": "A",
    "hint": "Which data would help you understand experiences?",
    "lesson": "Use qualitative work to answer an explanatory question, then integrate the findings deliberately.",
    "explanation": "Purposefully sample a range of perspectives, examine the narratives systematically and reconcile themes with the observed data.",
    "help": "basics/bias.htm",
    "operation": "",
    "lessonId": "study-designs",
    "difficulty": "intermediate",
    "learnerGoals": [
      "undergraduateMedicine",
      "postgraduateMedicine",
      "publicHealth",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "nih-mixed"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "fa67de81ea8cb8b1a66e8656efc6534ab5889fb80f679758534e1a12204c4a19"
    }
  },
  {
    "id": "BIO-INTEGRITY-01",
    "version": 1,
    "track": "researcher",
    "topic": "Research conduct",
    "objective": "Explain why single-test thresholds cannot be reused at every interim look.",
    "stem": "A study plans to inspect accumulating outcome data each month and stop for efficacy at the first unadjusted P \u003C 0.05. What is the main statistical concern with this rule?",
    "options": [
      {
        "id": "A",
        "text": "The repeated looks can inflate the chance of a false positive.",
        "feedback": "A planned sequential design can control this probability while allowing monitoring."
      },
      {
        "id": "B",
        "text": "Each look must be independent because a new month has passed.",
        "feedback": "The data commonly overlap across looks."
      },
      {
        "id": "C",
        "text": "The null hypothesis automatically changes with each look.",
        "feedback": "The same hypothesis may be tested repeatedly."
      },
      {
        "id": "D",
        "text": "A smaller P value always identifies the right treatment decision.",
        "feedback": "Results must account for monitoring and be interpreted alongside effects and harms."
      },
      {
        "id": "E",
        "text": "An ethics review alone adjusts the significance level.",
        "feedback": "Ethical and statistical oversight are related but distinct."
      }
    ],
    "correct": "A",
    "hint": "How many opportunities are there to stop by chance?",
    "lesson": "Specify how repeated looks and stopping rules affect inference before trial results are known.",
    "explanation": "Repeated stopping opportunities can inflate the false-positive probability. Use a prespecified group-sequential or other suitable monitoring method that accounts for dependence.",
    "help": "basics/bias.htm",
    "operation": "",
    "lessonId": "research-integrity",
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "ich-e9"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "28f95110e262783ba1d776c2eef802968a8f1073fd57f25c80471b1a7472b076"
    }
  },
  {
    "id": "BIO-INTEGRITY-02",
    "version": 1,
    "track": "researcher",
    "topic": "Research conduct",
    "objective": "Recognise re-identification risks when sharing analysis examples.",
    "stem": "A trainee wants to send a dataset to a tutor. Names have been removed, but exact visit dates, rare diagnoses and free-text case notes remain. What is the best way to share a reproducible worked example with this tutor?",
    "options": [
      {
        "id": "A",
        "text": "Send it because the name column has been removed.",
        "feedback": "Remaining variables can identify participants."
      },
      {
        "id": "B",
        "text": "Send only the free-text notes.",
        "feedback": "Notes can contain direct or indirect identifiers."
      },
      {
        "id": "C",
        "text": "Replace the names with participant IDs and email the file.",
        "feedback": "Pseudonymisation does not by itself authorise disclosure."
      },
      {
        "id": "D",
        "text": "Use synthetic data that reproduce the analysis structure, and keep protected data within authorised systems.",
        "feedback": "This lets the tutor check methods while protecting participants."
      },
      {
        "id": "E",
        "text": "Convert the worksheet to an image before sending.",
        "feedback": "The medium does not remove identifying content."
      }
    ],
    "correct": "D",
    "hint": "Can you reproduce the method without sharing an actual row?",
    "lesson": "Share code, definitions and synthetic examples when real participant data are not authorised for the recipient.",
    "explanation": "Exact dates, uncommon diagnoses and free text can identify people. Synthetic data can illustrate the method without disclosing those records.",
    "help": "basics/bias.htm",
    "operation": "",
    "lessonId": "research-integrity",
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "usmle-2026"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "433b971be192d38942ec6f5f4dd6b43607edfb36e4c9b4aae3a12d2a8172926d"
    }
  },
  {
    "id": "BIO-INTEGRITY-03",
    "version": 2,
    "track": "researcher",
    "topic": "Research conduct",
    "objective": "Distinguish prespecified and exploratory findings.",
    "stem": "The prespecified comparison for the registered primary outcome did not meet the trial\u2019s significance threshold. After seeing the results, investigators found a favourable subgroup and want to call that comparison the prespecified primary finding. What is the best response?",
    "options": [
      {
        "id": "A",
        "text": "Redefine the primary analysis after seeing the subgroup result.",
        "feedback": "This conceals the selection process."
      },
      {
        "id": "B",
        "text": "Report only the subgroup because it has the smallest P value.",
        "feedback": "Selective reporting distorts the evidence."
      },
      {
        "id": "C",
        "text": "Report the registered outcome, disclose the exploratory subgroup search and seek independent confirmation.",
        "feedback": "Readers can distinguish planned evidence from a hypothesis generated by the data."
      },
      {
        "id": "D",
        "text": "Declare the subgroup result false without further investigation.",
        "feedback": "An exploratory result can suggest a useful hypothesis."
      },
      {
        "id": "E",
        "text": "Report that subgroup and overall effects are different without an interaction comparison.",
        "feedback": "A significant result in one subgroup and a nonsignificant result elsewhere do not establish interaction. Compare effect estimates directly with an appropriate interaction test."
      }
    ],
    "correct": "C",
    "hint": "Separate analyses agreed before seeing data from those prompted by the data.",
    "lesson": "A clear record of decisions allows readers to judge the evidence.",
    "explanation": "Keep the original outcome visible, label the analysis exploratory, account for the wider search and do not present the subgroup as confirmatory.",
    "help": "basics/bias.htm",
    "operation": "",
    "lessonId": "research-integrity",
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "ich-e9"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "47d0273d1eb32eeae5fd71ffa754ebc48edcf81c2468f1bd5262c2b4ad6f1729"
    }
  },
  {
    "id": "BIO-CAUSAL-04",
    "version": 1,
    "track": "researcher",
    "topic": "Causal inference",
    "objective": "Use a DAG to choose adjustment for a total treatment effect.",
    "stem": "For a fictional treatment T and later admission Y, the assumed arrows are A \u2192 T, A \u2192 Y, T \u2192 Y, T \u2192 M, M \u2192 Y, T \u2192 F, U \u2192 F and U \u2192 Y. A is age, M is early improvement, F is follow-up attendance and U is access to care. Which approach estimates the total treatment effect under this graph, given complete outcome measurement, overlap and consistency?",
    "options": [
      {
        "id": "A",
        "text": "Adjust for age and early improvement.",
        "feedback": "Early improvement mediates part of the effect that the total-effect question includes."
      },
      {
        "id": "B",
        "text": "Adjust for age alone.",
        "feedback": "Age blocks the backdoor path. Leaving M and F unconditioned preserves mediation and avoids opening the collider path."
      },
      {
        "id": "C",
        "text": "Adjust for age and restrict to follow-up attendees.",
        "feedback": "Conditioning on attendance opens T \u2192 F \u2190 U \u2192 Y."
      },
      {
        "id": "D",
        "text": "Control only for access to care.",
        "feedback": "Access does not close T \u2190 A \u2192 Y."
      },
      {
        "id": "E",
        "text": "Use the unadjusted contrast.",
        "feedback": "Age affects both treatment and admission under the stated assumptions."
      }
    ],
    "correct": "B",
    "hint": "Trace the arrows entering T, then check where two arrows enter the same node.",
    "lesson": "A DAG helps distinguish a confounder from a mediator and a collider.",
    "explanation": "Age is the stated common cause of treatment and outcome. Adjustment for age closes the backdoor path. Conditioning on improvement removes a mediated pathway, and conditioning on attendance opens a path through access to care.",
    "help": "basics/confounding.htm",
    "operation": "",
    "lessonId": "causal-adjustment",
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "what-if"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "c5d4a43a84f02f45eb7426f29e66aaa71cabb5ca509325a967614bdff6615ec7"
    }
  },
  {
    "id": "BIO-SYNTH-04",
    "version": 1,
    "track": "researcher",
    "topic": "Evidence synthesis",
    "objective": "Read an individual line, pooled diamond and prediction interval on a forest plot.",
    "stem": "A forest plot shows days to recovery, intervention minus usual care. One study\u0027s line spans \u22122.96 to \u002B0.96 days. The pooled diamond spans \u22123.96 to \u22120.04 days. The separate prediction interval spans \u22125.93 to \u002B1.93 days. Which reading is best?",
    "options": [
      {
        "id": "A",
        "text": "The diamond shows that every study found a shorter recovery time.",
        "feedback": "The diamond represents the pooled estimate and its interval. Read individual rows separately."
      },
      {
        "id": "B",
        "text": "The prediction interval estimates uncertainty about the average effect.",
        "feedback": "The diamond represents this uncertainty. The prediction interval includes variation in true effects across settings."
      },
      {
        "id": "C",
        "text": "The individual study\u0027s confidence interval shows an effect of exactly zero.",
        "feedback": "The interval includes zero and both directions. It does not establish that the true effect is zero."
      },
      {
        "id": "D",
        "text": "The pooled interval favours shorter recovery on average, while a comparable new setting could see a longer recovery time.",
        "feedback": "The diamond is below zero, while the prediction interval extends above zero."
      },
      {
        "id": "E",
        "text": "The upper edge of the diamond rules out effects larger than zero in any comparable setting.",
        "feedback": "A confidence interval for the average does not bound the plausible true effect in each setting."
      }
    ],
    "correct": "D",
    "hint": "Zero represents no difference in recovery time. Identify which quantity each interval describes.",
    "lesson": "The individual interval, pooled confidence interval and prediction interval answer different questions.",
    "explanation": "The study line includes both shorter and longer recovery. The pooled confidence interval covers average effects below zero. The prediction interval includes zero and longer recovery times, conditional on the model.",
    "help": "meta_analysis/summary.htm",
    "operation": "",
    "lessonId": "evidence-synthesis",
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "sourceIds": [
      "cochrane-meta"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "6bb788627db963668f07b29fa73cb61c53e20be3328155a7a9fc81e2cddf77c8"
    }
  },
  {
    "id": "MULTI-IND-01",
    "version": 1,
    "track": "medicine",
    "topic": "Unequal group variances",
    "objective": "Choose a mean comparison under heteroscedasticity.",
    "stem": "Three independent groups of adults complete different fictional rehabilitation programmes. Each person gives one continuous score. Each group\u0027s distribution is roughly symmetric, the sample sizes and variances differ, and the planned question concerns any difference in population means. Which overall method fits the question?",
    "options": [
      {
        "id": "A",
        "text": "A paired t test",
        "feedback": "This design has neither pairs nor a comparison limited to two means."
      },
      {
        "id": "B",
        "text": "Ordinary ANOVA with a common variance",
        "feedback": "The common-variance model conflicts with the stated unequal variances."
      },
      {
        "id": "C",
        "text": "Welch one-way ANOVA",
        "feedback": "It targets equality of independent group means without pooling within-group variance."
      },
      {
        "id": "D",
        "text": "Friedman test",
        "feedback": "This requires related observations arranged in blocks."
      },
      {
        "id": "E",
        "text": "Kruskal\u2013Wallis rank test",
        "feedback": "It compares groups on the rank scale and does not directly target equality of arithmetic means."
      }
    ],
    "correct": "C",
    "hint": "Hold the target quantity fixed when deciding how to handle unequal variances.",
    "lesson": "Welch\u0027s method retains the means as its target while allowing unequal group variances.",
    "explanation": "The three groups are independent and the estimand concerns means. Welch one-way ANOVA addresses that global comparison with separate variance estimates and approximate degrees of freedom.",
    "lessonId": "independent-group-comparisons",
    "sourceIds": [
      "dfph-syllabus",
      "r-oneway.test"
    ],
    "help": "analysis_of_variance/one_way.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "5b16a5771289f4d793d865af41544b5392f54707074c029b26c6385b8ab74baa"
    }
  },
  {
    "id": "MULTI-IND-02",
    "version": 1,
    "track": "medicine",
    "topic": "Omnibus result and pairwise inference",
    "objective": "Interpret a global F test and follow-up intervals.",
    "stem": "An ordinary ANOVA comparing mean scores across three training formats gives P = 0.002. The investigators also report a 95% Tukey simultaneous confidence interval of \u22120.4 to \u002B3.4 points for written training minus usual training. Which interpretation is best supported?",
    "options": [
      {
        "id": "A",
        "text": "Every pair of population means differs.",
        "feedback": "The overall test cannot identify specific pairs."
      },
      {
        "id": "B",
        "text": "The probability the written and usual means are equal is 0.002.",
        "feedback": "The overall P value is not a posterior probability and is not specific to this pair."
      },
      {
        "id": "C",
        "text": "Written training improves the mean by exactly 1.5 points.",
        "feedback": "The interval midpoint is a point estimate with appreciable uncertainty."
      },
      {
        "id": "D",
        "text": "The overall test gives evidence against all means being equal. The simultaneous comparison does not establish a written-versus-usual difference.",
        "feedback": "The small global P value and a pairwise interval that includes zero can coexist."
      },
      {
        "id": "E",
        "text": "Written and usual training have been shown equivalent.",
        "feedback": "A confidence interval crossing zero does not establish equivalence. An equivalence claim requires justified margins and the appropriate analysis."
      }
    ],
    "correct": "D",
    "hint": "Separate the global null from the specific contrast and inspect its interval.",
    "lesson": "An omnibus result can support an overall difference even when a chosen contrast is uncertain.",
    "explanation": "The data provide evidence against equality of all three means. The simultaneous interval for written versus usual includes zero, so that pair\u0027s difference is not established by this analysis.",
    "lessonId": "independent-group-comparisons",
    "sourceIds": [
      "rcpsych-2027",
      "r-tukeyhsd"
    ],
    "help": "analysis_of_variance/multiple_comparisons.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics",
      "usMedicalBoards"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "8766df6417317d201c8346994aaafa718e5ac4968149efeba914d8e74777bab7"
    }
  },
  {
    "id": "MULTI-IND-03",
    "version": 1,
    "track": "medicine",
    "topic": "Baseline-adjusted means",
    "objective": "Recognise a continuous-outcome comparison with a baseline covariate.",
    "stem": "A four-arm randomised study measures the same symptom score at baseline and 12 weeks. The protocol prespecifies a comparison of mean 12-week score adjusting for baseline score, using one common baseline slope for all arms. Which model has that structure?",
    "options": [
      {
        "id": "A",
        "text": "ANCOVA with 12-week score as outcome, treatment as a factor and baseline score as a covariate",
        "feedback": "This fits a baseline-adjusted comparison conditional on the proposed common slope."
      },
      {
        "id": "B",
        "text": "One-way ANOVA with 12-week score and treatment only",
        "feedback": "This compares groups without the prespecified baseline adjustment."
      },
      {
        "id": "C",
        "text": "MANOVA jointly analysing baseline and 12-week scores as two responses",
        "feedback": "Two responses do not specify the requested baseline covariate model."
      },
      {
        "id": "D",
        "text": "Kruskal\u2013Wallis test of baseline scores",
        "feedback": "This neither models the 12-week outcome nor estimates the requested adjusted mean contrasts."
      },
      {
        "id": "E",
        "text": "Friedman test treating treatment arms as repeated measurements on each participant",
        "feedback": "Each participant received one arm. Baseline and follow-up do not make the arms repeated conditions."
      }
    ],
    "correct": "A",
    "hint": "Ask which measurement is the outcome and which is the adjustment variable.",
    "lesson": "A baseline covariate and a follow-up outcome play different roles in a regression model.",
    "explanation": "Use ANCOVA with treatment indicators and a baseline covariate, then report prespecified adjusted contrasts and their intervals. Check whether the common-slope model is plausible.",
    "lessonId": "independent-group-comparisons",
    "sourceIds": [
      "rcpsych-2027",
      "aba-interpretation"
    ],
    "help": "regression_and_correlation/grouped_covariance.htm",
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "94049a813e79cdb0297a029c43a0e4e61b09b8d28fea7b41c1ad413dd30f083f"
    }
  },
  {
    "id": "MULTI-REP-01",
    "version": 1,
    "track": "medicine",
    "topic": "Matched ordinal responses",
    "objective": "Select a global rank comparison for several matched conditions.",
    "stem": "Each of 18 participants rates the comfort of three fictional devices on a five-level ordered scale. Device order is randomised for each participant, every rating is observed, and a global comparison using within-person ranks is planned. Which test fits?",
    "options": [
      {
        "id": "A",
        "text": "Kruskal\u2013Wallis",
        "feedback": "This pools ranks across independent groups, whereas the device scores are matched by participant."
      },
      {
        "id": "B",
        "text": "Cochran Q",
        "feedback": "It compares matched binary responses. The comfort scale has ordered categories beyond two levels."
      },
      {
        "id": "C",
        "text": "Welch one-way ANOVA",
        "feedback": "This compares independent group means and does not use the ranking within each participant."
      },
      {
        "id": "D",
        "text": "Friedman test",
        "feedback": "Rank the three scores within each participant and compare the conditions across the complete independent blocks."
      },
      {
        "id": "E",
        "text": "Wilcoxon signed-rank test for one pair",
        "feedback": "A single comparison of two related conditions does not address the global three-condition question."
      }
    ],
    "correct": "D",
    "hint": "Identify where the ranking needs to happen and how many conditions are compared.",
    "lesson": "Friedman is a global within-block rank comparison for repeated ordered responses.",
    "explanation": "A complete row is one participant. Rank the device scores inside each row and compare their rank patterns over all participants.",
    "lessonId": "repeated-condition-comparisons",
    "sourceIds": [
      "rcpsych-2027",
      "r-friedman.test"
    ],
    "help": "nonparametric_methods/friedman.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "097ba0b75dcf742fee53978de51e153e68032e5a95b2bf5d0fc819f9f83f5d8c"
    }
  },
  {
    "id": "MULTI-REP-02",
    "version": 1,
    "track": "medicine",
    "topic": "Sphericity",
    "objective": "Recognise the covariance assumption of an uncorrected F test.",
    "stem": "The same adults complete each of three measurement conditions. Analysts propose the usual uncorrected univariate repeated-measures ANOVA F test. Which statement describes sphericity?",
    "options": [
      {
        "id": "A",
        "text": "The condition means are equal.",
        "feedback": "That is the global mean null hypothesis, not the covariance assumption."
      },
      {
        "id": "B",
        "text": "The population variances of all pairwise within-person differences are equal.",
        "feedback": "This is sphericity for a single within-person factor."
      },
      {
        "id": "C",
        "text": "Each participant\u0027s repeated observations are independent.",
        "feedback": "A repeated design allows dependence within the same person."
      },
      {
        "id": "D",
        "text": "The three marginal variances alone are equal.",
        "feedback": "Univariate marginal variances do not determine the variances of the differences, which also depend on covariance."
      },
      {
        "id": "E",
        "text": "The observed ranks have no ties.",
        "feedback": "Ties affect rank calculations. Sphericity concerns variances of contrasts in the mean model."
      }
    ],
    "correct": "B",
    "hint": "Think about what varies when the same person\u0027s measurements are subtracted.",
    "lesson": "Sphericity concerns within-participant differences, which depend on variances and covariances.",
    "explanation": "For three conditions it requires equal population variances of A\u2212B, A\u2212C and B\u2212C. A valid correction or a model with an appropriate covariance structure may be needed when it is untenable.",
    "lessonId": "repeated-condition-comparisons",
    "sourceIds": [
      "r-aov"
    ],
    "help": "analysis_of_variance/two_way.htm",
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "1a8e4d8025d2614ea51bdd4c949949c54a6a615a7d527ea5c94ca063297dafd5"
    }
  },
  {
    "id": "MULTI-REP-03",
    "version": 1,
    "track": "medicine",
    "topic": "Longitudinal treatment comparisons",
    "objective": "Choose a model that addresses trajectories with incomplete follow-up.",
    "stem": "In a fictional two-arm trial, symptom scores are scheduled at baseline, 1 month, 3 months and 6 months. Some follow-ups are missed. The primary question is whether mean trajectories differ by arm. Which strategy most directly addresses this target?",
    "options": [
      {
        "id": "A",
        "text": "Compare the groups separately at each observed visit using unadjusted independent tests.",
        "feedback": "Separate tests do not estimate the planned trajectory interaction and need a multiplicity plan."
      },
      {
        "id": "B",
        "text": "Replace every missing score with zero, then analyse the independent rows.",
        "feedback": "Zero is an assumption about an unobserved outcome and repeated rows for each person are dependent."
      },
      {
        "id": "C",
        "text": "Test change over time separately in each arm and infer a treatment difference if only one is significant.",
        "feedback": "A difference in statistical significance does not show a difference between arms. Estimate the arm-by-visit contrast directly."
      },
      {
        "id": "D",
        "text": "Compare each person\u0027s last recorded score regardless of visit date.",
        "feedback": "Last observed values can represent different times and a changed target population."
      },
      {
        "id": "E",
        "text": "Use a suitable longitudinal model with arm, visit and their interaction, accounting for within-person correlation and missingness assumptions.",
        "feedback": "It addresses mean trajectories and makes both the dependency and missing-data assumptions explicit."
      }
    ],
    "correct": "E",
    "hint": "Choose a method that represents treatment, time and observations from the same person together.",
    "lesson": "A trajectory comparison is a treatment-by-time question, with a plan for within-person dependence and incomplete data.",
    "explanation": "A suitable longitudinal model can use observed repeated outcomes on their intended schedule and estimate treatment-by-visit contrasts. Check the chosen covariance and missingness assumptions and consider sensitivity analyses.",
    "lessonId": "repeated-condition-comparisons",
    "sourceIds": [
      "rcpsych-2027",
      "dfph-syllabus"
    ],
    "help": "analysis_of_variance/two_way.htm",
    "difficulty": "advanced",
    "learnerGoals": [
      "postgraduateMedicine",
      "publicHealth",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "89857be5af82f6ef9918afe3e7a96715f7e25afc37163f121207eaa748be9f4d"
    }
  },
  {
    "id": "BIO-SURVEY-01",
    "version": 1,
    "track": "publicHealth",
    "topic": "Survey population weights",
    "objective": "Estimate a proportion with unequal sampling fractions.",
    "stem": "A fictional city has 800 eligible people in one stratum and 200 in another. Independent random samples of 100 from each yield 15 and 45 people with the outcome. What is the population-weighted proportion estimate?",
    "options": [
      {
        "id": "A",
        "text": "12%",
        "feedback": "This is only the contribution from the larger stratum."
      },
      {
        "id": "B",
        "text": "21%",
        "feedback": "Weight 15% and 45% by the population shares 0.8 and 0.2."
      },
      {
        "id": "C",
        "text": "30%",
        "feedback": "This uses the equal sample sizes as weights."
      },
      {
        "id": "D",
        "text": "45%",
        "feedback": "This is only the estimate in the smaller stratum."
      },
      {
        "id": "E",
        "text": "60%",
        "feedback": "This sums the stratum estimates without weighting."
      }
    ],
    "correct": "B",
    "hint": "Use population shares, then add the two contributions.",
    "lesson": "Known population shares combine the stratum estimates.",
    "explanation": "0.8 \u00D7 15% \u002B 0.2 \u00D7 45% = 21%.",
    "lessonId": "complex-surveys",
    "sourceIds": [
      "nhanes-weighting"
    ],
    "help": "sample_size/population_survey.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "publicHealth",
      "mastersStatistics",
      "healthcareBusinessAnalyst"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "86243ca487f161b2e485c408c4c5351de6ca79829cded5002f213b2f7e25d3bb"
    }
  },
  {
    "id": "BIO-SURVEY-02",
    "version": 1,
    "track": "publicHealth",
    "topic": "Cluster survey precision",
    "objective": "Identify the source of information for survey variance.",
    "stem": "In a stratified survey, each stratum draws a simple random sample of clinics without replacement. Each clinic has the same number of eligible people and all people in sampled clinics respond. Which approach estimates the standard error of the overall proportion?",
    "options": [
      {
        "id": "A",
        "text": "Calculate an unweighted binomial standard error using all people",
        "feedback": "That treats people as the independent sampling units."
      },
      {
        "id": "B",
        "text": "Pool clinic proportions and use one overall clinic sampling fraction.",
        "feedback": "This ignores each stratum\u2019s sampling fraction and its squared weight in the variance of the combined estimate."
      },
      {
        "id": "C",
        "text": "Weight observations and then assume they are independently sampled",
        "feedback": "Correct point-estimate weights do not make within-clinic selections independent."
      },
      {
        "id": "D",
        "text": "Use variation among sampled clinic proportions within strata and apply the finite population correction",
        "feedback": "Clinics were sampled separately within each stratum. Combine stratum variances with squared population shares."
      },
      {
        "id": "E",
        "text": "Set the standard error to zero because all selected people responded",
        "feedback": "Complete response removes non-response here. There is still uncertainty from selecting a subset of clinics."
      }
    ],
    "correct": "D",
    "hint": "Identify the units randomly selected by the survey.",
    "lesson": "A probability sample of clinics can estimate sampling variability from sampled clinics within each stratum.",
    "explanation": "Use the sampled clinic proportions within strata, incorporate sampling fractions and then combine the independent stratum variances.",
    "lessonId": "complex-surveys",
    "sourceIds": [
      "dfph-syllabus",
      "nhanes-variance"
    ],
    "help": "sample_size/population_survey.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "publicHealth",
      "mastersStatistics",
      "healthcareBusinessAnalyst"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "55023b1319571cb847ff510ba9f52ac315c94c958260ed7c7d4976fc3a9a96d5"
    }
  },
  {
    "id": "BIO-ITS-01",
    "version": 1,
    "track": "publicHealth",
    "topic": "Level and trend change",
    "objective": "Combine model terms at a given post-intervention time.",
    "stem": "A segmented regression estimates an immediate change of \u22124 incidents per 1,000 visits and a slope change of \u22121.2 per 1,000 visits per month. Time since intervention is coded zero at the first post-intervention observation. What is the fitted contrast with continuing the baseline trend four months after that observation?",
    "options": [
      {
        "id": "A",
        "text": "\u22128.8 per 1,000 visits",
        "feedback": "Add the immediate change and four months of slope change."
      },
      {
        "id": "B",
        "text": "\u22124.8 per 1,000 visits",
        "feedback": "This omits the immediate change."
      },
      {
        "id": "C",
        "text": "\u22124.0 per 1,000 visits",
        "feedback": "This is the contrast at the start of the post-intervention period."
      },
      {
        "id": "D",
        "text": "\u22121.2 per 1,000 visits",
        "feedback": "This is the slope change per month rather than the contrast after four months."
      },
      {
        "id": "E",
        "text": "\u002B0.8 per 1,000 visits",
        "feedback": "This reverses the direction of the slope change."
      }
    ],
    "correct": "A",
    "hint": "At the first post-intervention observation, time since intervention is zero.",
    "lesson": "The contrast combines level change and elapsed time times slope change.",
    "explanation": "The contrast is \u22124 \u002B 4 \u00D7 (\u22121.2) = \u22128.8 per 1,000 visits.",
    "lessonId": "interrupted-time-series",
    "sourceIds": [
      "its-lopez-bernal"
    ],
    "help": "regression_and_correlation/multiple_linear.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "publicHealth",
      "mastersStatistics",
      "healthcareBusinessAnalyst"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "24e9a7e58004d1775496dbcdd51634f784e7d8b892683f0779a8b4b833f6c216"
    }
  },
  {
    "id": "BIO-ITS-02",
    "version": 1,
    "track": "publicHealth",
    "topic": "Changing denominators",
    "objective": "Use denominators when interpreting reported changes over time.",
    "stem": "A clinic recorded 10 incidents in 1,000 visits in one month and 20 incidents in 3,000 visits in another. Assume the recording method and coverage stayed the same. What can you say about the observed incidents per visit?",
    "options": [
      {
        "id": "A",
        "text": "They doubled",
        "feedback": "Only the incident count doubled."
      },
      {
        "id": "B",
        "text": "They tripled",
        "feedback": "The visit denominator tripled."
      },
      {
        "id": "C",
        "text": "They stayed constant",
        "feedback": "The numerator and denominator changed by different factors."
      },
      {
        "id": "D",
        "text": "They fell from 10 to about 6.7 per 1,000 visits",
        "feedback": "Calculate the count divided by visits in each month and multiply by 1,000."
      },
      {
        "id": "E",
        "text": "They cannot be calculated without each person\u0027s follow-up time",
        "feedback": "The requested denominator is the number of visits, which is supplied."
      }
    ],
    "correct": "D",
    "hint": "Divide each incident count by its own visit total.",
    "lesson": "A larger count can accompany a lower rate.",
    "explanation": "The observed rates are 10/1,000 and 20/3,000. This comparison alone cannot attribute the change to an intervention.",
    "lessonId": "interrupted-time-series",
    "sourceIds": [
      "dfph-syllabus",
      "its-lopez-bernal"
    ],
    "help": "regression_and_correlation/multiple_linear.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "publicHealth",
      "mastersStatistics",
      "healthcareBusinessAnalyst"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "78d19df77e2bf18701bae7588d38389d95b170a9f0fe23df7070fbedfdc446c8"
    }
  },
  {
    "id": "BIO-SPATIAL-01",
    "version": 1,
    "track": "publicHealth",
    "topic": "Comparing rate and count maps",
    "objective": "Choose a measure appropriate for geographic comparison.",
    "stem": "Two districts have the same surveillance coverage and age mix. District A has 30 new diagnoses among 3,000 people at risk. District B has 80 among 20,000. Which interpretation is correct for this period?",
    "options": [
      {
        "id": "A",
        "text": "District B has the higher observed risk because 80 is larger than 30",
        "feedback": "This compares counts only."
      },
      {
        "id": "B",
        "text": "Both risks are equal because they use the same case definition",
        "feedback": "Comparable case definitions do not make the risks equal."
      },
      {
        "id": "C",
        "text": "District A has the higher observed risk, although District B has more diagnoses",
        "feedback": "A has 10 per 1,000 and B has 4 per 1,000."
      },
      {
        "id": "D",
        "text": "Neither observed risk can be calculated until every district has the same number of residents",
        "feedback": "Different denominator sizes can be compared using proportions."
      },
      {
        "id": "E",
        "text": "There is proof of a causal effect of living in District A",
        "feedback": "These summaries cannot rule out other explanations for the geographic difference."
      }
    ],
    "correct": "C",
    "hint": "Divide each count by its own district denominator.",
    "lesson": "The rate or risk can rank places differently from a count.",
    "explanation": "The observed proportion is 1% in A and 0.4% in B. The case count is larger in B.",
    "lessonId": "small-area-rates",
    "sourceIds": [
      "cdc-gis"
    ],
    "help": "rates/direct_standardization.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "publicHealth",
      "mastersStatistics",
      "healthcareBusinessAnalyst"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "b31ef3b3b40ba6f4b2b205751b29c781e631750f5a0a1692588fbd214fbdeeee"
    }
  },
  {
    "id": "BIO-SPATIAL-02",
    "version": 1,
    "track": "publicHealth",
    "topic": "Small-area uncertainty",
    "objective": "Interpret a high rate based on sparse data.",
    "stem": "A small area has one case among 100 residents at risk. A larger area has 100 cases among 20,000. The mapping system applies the same case definition and period in each. Which response is most appropriate?",
    "options": [
      {
        "id": "A",
        "text": "Conclude the smaller area has twice the underlying risk",
        "feedback": "The observed estimates are in a 2:1 ratio, but the small area\u0027s rate is uncertain."
      },
      {
        "id": "B",
        "text": "Display the smaller area\u0027s value as zero",
        "feedback": "A sparse or suppressed result is distinct from zero."
      },
      {
        "id": "C",
        "text": "Investigate only if the small area has 100 cases",
        "feedback": "A single count threshold cannot decide whether a pattern warrants investigation."
      },
      {
        "id": "D",
        "text": "Use only raw counts to compare risk",
        "feedback": "Raw counts reflect population size as well as disease frequency."
      },
      {
        "id": "E",
        "text": "Examine uncertainty and detection before interpreting the difference",
        "feedback": "One additional case would double the smaller area\u0027s rate under the fixed denominator. Ascertainment can also differ."
      }
    ],
    "correct": "E",
    "hint": "Consider how moving one case affects each estimate.",
    "lesson": "Sparse counts can change a rank even if the population at risk is well measured.",
    "explanation": "The rates are 10 and 5 per 1,000. The former rests on one event, so seek comparable ascertainment and display or otherwise address uncertainty.",
    "lessonId": "small-area-rates",
    "sourceIds": [
      "cdc-gis",
      "dfph-syllabus"
    ],
    "help": "rates/direct_standardization.htm",
    "difficulty": "intermediate",
    "learnerGoals": [
      "publicHealth",
      "mastersStatistics",
      "healthcareBusinessAnalyst"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "7ae2adc0bb1c2b03e290bc6e8922272493fa84a8a031d18af316a7b24cd3ce80"
    }
  },
  {
    "id": "BIO-ECON-01",
    "version": 1,
    "track": "publicHealth",
    "topic": "Incremental cost per QALY",
    "objective": "Use the comparator to calculate an incremental ratio.",
    "stem": "After discounting, a fictional new programme costs \u00A32,400 and yields 1.60 QALYs per person. Current care costs \u00A32,000 and yields 1.55 QALYs over the same horizon. What is the incremental cost per additional QALY?",
    "options": [
      {
        "id": "A",
        "text": "\u00A3250",
        "feedback": "This divides the \u00A3400 increment by the new programme\u0027s total QALYs."
      },
      {
        "id": "B",
        "text": "\u00A3400",
        "feedback": "This is the incremental cost per person."
      },
      {
        "id": "C",
        "text": "\u00A31,500",
        "feedback": "This divides the new programme\u0027s total cost by its total QALYs."
      },
      {
        "id": "D",
        "text": "\u00A38,000",
        "feedback": "Divide the \u00A3400 difference in cost by the 0.05 difference in QALYs."
      },
      {
        "id": "E",
        "text": "\u00A348,000",
        "feedback": "This divides the new programme\u0027s total cost by the incremental QALYs."
      }
    ],
    "correct": "D",
    "hint": "Compute differences in both columns before dividing.",
    "lesson": "Incremental comparisons use differences in both costs and outcomes.",
    "explanation": "The increment is (\u00A32,400 \u2212 \u00A32,000)/(1.60 \u2212 1.55) = \u00A38,000 per additional QALY.",
    "lessonId": "health-economic-evaluation",
    "sourceIds": [
      "dfph-syllabus",
      "nice-economic-evaluation"
    ],
    "help": "",
    "difficulty": "intermediate",
    "learnerGoals": [
      "publicHealth",
      "healthcareBusinessAnalyst",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "d914ae0cf066150a9d29868fc978c3e1f1c208dce9b8c7cc33e021560473b639"
    }
  },
  {
    "id": "BIO-ECON-02",
    "version": 1,
    "track": "publicHealth",
    "topic": "Perspective and sensitivity",
    "objective": "Identify how a broader perspective changes the analysis.",
    "stem": "A fictional programme reduces unpaid family care, but its original evaluation included only the provider\u0027s costs. What would address this limitation if a broader perspective is requested?",
    "options": [
      {
        "id": "A",
        "text": "Include a stated valuation of changes in unpaid care, and vary that assumption in sensitivity analysis",
        "feedback": "A broader perspective can count the value of unpaid care. State how it is measured and valued, and check uncertainty."
      },
      {
        "id": "B",
        "text": "Use a different discount rate without adding unpaid care to the resource inventory.",
        "feedback": "Discounting changes the value assigned to resources over time. It does not account for omitted unpaid care."
      },
      {
        "id": "C",
        "text": "Treat the time as zero cost because no invoice was issued",
        "feedback": "Unpaid time can have an opportunity cost even without a payment."
      },
      {
        "id": "D",
        "text": "Compare the intervention with no care even though current care is available",
        "feedback": "A realistic alternative is still needed."
      },
      {
        "id": "E",
        "text": "Assume the programme saves money because care time falls",
        "feedback": "Other costs can offset the value of the reduction. Estimate the costs before concluding."
      }
    ],
    "correct": "A",
    "hint": "Ask which resources the requested perspective counts.",
    "lesson": "Perspective determines which resource consequences enter the evaluation.",
    "explanation": "Measure the change in unpaid care with a clear valuation method and show how different assumptions affect the conclusion.",
    "lessonId": "health-economic-evaluation",
    "sourceIds": [
      "dfph-syllabus",
      "nice-economic-evaluation"
    ],
    "help": "",
    "difficulty": "intermediate",
    "learnerGoals": [
      "publicHealth",
      "healthcareBusinessAnalyst",
      "mastersStatistics"
    ],
    "review": {
      "status": "Reviewed for release",
      "date": "2026-11-15",
      "reviewer": "Codex review; Iain Buchan release approval",
      "notes": "Answer key, distractors and feedback independently checked.",
      "contentSha256": "bdaa72763ca1c3c27c5e094972cf841077673581730db6325877bc5f9f52e2a0"
    }
  }
];
