// Official sources checked 15 November 2026. Link to examination material without embedding items.
export const examGroups=[
 {title:'UK medical school and postgraduate exams',sources:[
  {title:'Medical Licensing Assessment: practice papers',url:'https://www.medschools.ac.uk/for-students/medical-licensing-assessment/practice-exam-for-the-ms-akt/',note:'Practise interpreting results and making clinical decisions from data. The public papers show the single-best-answer format.'},
  {title:'MRCP(UK) Part 1: blueprint',url:'https://www.thefederation.uk/examinations/part-1/format',note:'The combined topic covers statistics, epidemiology and evidence-based medicine. The official Part 1 sample requires registration.'},
  {title:'MRCGP AKT: preparation and sample questions',url:'https://www.rcgp.org.uk/mrcgp-exams/applied-knowledge-test/akt-preparing',note:'Practise risk, charts and evidence interpretation in primary care.'},
  {title:'MRCPsych: Paper B syllabus and Critical Review',url:'https://www.rcpsych.ac.uk/training/exams/preparing-for-exams',note:'Appraise study design, statistical methods and reporting. Check the syllabus for your sitting, especially the January 2027 change.'},
  {title:'MRCOG: syllabus and knowledge requirements',url:'https://www.rcog.org.uk/media/yeqbkfrw/mrcog-syllabus-and-knowledge-requirements-v10.pdf',note:'Practise screening, maternal and perinatal denominators, trial design and appraisal.'},
  {title:'MRCS: candidate guidance',url:'https://www.intercollegiatemrcsexams.org.uk/about-the-mrcs/candidate-guidance/',note:'The Part A blueprint includes data interpretation and audit.'},
  {title:'MRCPCH: theory syllabi',url:'https://www.rcpch.ac.uk/sites/default/files/2026-02/MRCPCH-theory-exam-syllabi-2023-v3.pdf#page=60',note:'Theory and Science addresses methods and screening. Applied Knowledge includes interpretation of evidence.'},
  {title:'FRCA: Research and Managing Data',url:'https://www.rcoa.ac.uk/documents/examination-syllabus-stage-1/research-managing-data-rd',note:'Practise distributions, inference, power, accuracy and regression. For Final FRCA, add appraisal of trials and reviews.'},
  {title:'FRCEM: official journal appraisal exercise',url:'https://rcem.ac.uk/wp-content/uploads/2022/08/FRCEM_OSCE_Sample_Journal_Article_and_Questions.pdf',note:'Rehearse explaining selection, masking, accuracy and applicability. MRCEM Primary additionally names statistics and research methods.'},
  {title:'FFICM: exam syllabus',url:'https://www.ficm.ac.uk/sites/ficm/files/documents/2022-03/FFICM%20Exam%20Syllabus%20v1.0%20March%202022.pdf',note:'Practise estimation, test choice, clinical study design and evidence synthesis. Check the examination format for your sitting.'},
  {title:'Specialty Certificate Examinations: specialty blueprints',url:'https://www.thefederation.uk/examinations/specialty-certificate-examinations/specialties',note:'Use your specialty’s documents. The statistical content and assessment categories differ by specialty.'},
  {title:'MSRA: Clinical Problem Solving',url:'https://medical.hee.nhs.uk/medical-training-recruitment/medical-specialty-training/multi-specialty-recruitment-assessment-msra/whats-in-the-msra/clinical-problem-solving-paper',note:'This is a recruitment assessment. The published remit does not identify a separate statistics section.'}
 ]},
 {title:'Public health exams',sources:[
  {title:'DFPH: specimen papers and marking schemes',url:'https://www.fph.org.uk/training-careers/the-diplomate-dfph-and-final-membership-examination-mfph/the-diplomate-examination-dfph/the-diplomate-examination-dfph-preparation/',note:'Combine calculations with interpretation of tables and graphs, critical appraisal and short written responses.'},
  {title:'MFPH: practice stations',url:'https://www.fph.org.uk/training-careers/the-diplomate-dfph-and-final-membership-examination-mfph/the-faculty-of-public-health-final-membership-examination/practice/',note:'Practise explaining evidence and uncertainty in a role-play with a defined audience.'},
  {title:'ABPM: Public Health and General Preventive Medicine',url:'https://www.theabpm.org/become-certified/exam-content/public-health-and-general-preventive-medicine-content-outline/',note:'The specialty outline gives separate percentages for epidemiology, biostatistics and informatics in one combined domain.'}
 ]},
 {title:'US medical licensure and board certification',sources:[
  {title:'USMLE: common content outline',url:'https://www.usmle.org/sites/default/files/2022-01/USMLE_Content_Outline_0.pdf#page=37',note:'Use the biostatistics, epidemiology, population health and medical literature section. Each Step has its own specifications.'},
  {title:'USMLE Step 3: formats and sample questions',url:'https://www.usmle.org/exam-resources/step-3-materials/step-3-formats-questions',note:'Practise interpreting abstracts, relative and absolute risks, and evidence claims in drug advertisements.'},
  {title:'ABMS: specialty and subspecialty certificates',url:'https://abms.org/member-boards/specialty-subspecialty-certificates/',note:'Find the board for your specialty and check its initial certification blueprint. USMLE is a licensing exam; board certification has separate requirements.'},
  {title:'ABIM: internal medicine and subspecialty blueprints',url:'https://www.abim.org/about/abim-exams/blueprints/',note:'Choose the certification blueprint that applies to your assessment. Clinical epidemiology and prognosis may be tested within clinical scenarios.'}
 ]}
];

export const communicationPrompt='Practise a short public-health communication scenario with me. You are a fictional service manager asking whether a clinic with longer average waiting times is performing poorly. Give me a small original data example, ask me to explain it in plain language, and wait for my reply. Then give formative feedback on interpretation, case mix, uncertainty and clarity. Do not assign an official examination grade.';
