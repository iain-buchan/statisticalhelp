#!/usr/bin/perl
# Adds the empty "R code" section for web and compiled help to a help topic if it has none yet.
# It goes before the run of see-also links at the end of the topic (after the example's discussion).
# Then insert-rcode.pl fills it.   usage: perl add-rcode-section.pl <topic.htm>
use strict;
use warnings;

my ($topic) = @ARGV;
open(my $t, '<:raw', $topic) or die "cannot read $topic";
local $/;
my $html = <$t>;
close $t;
if ($html =~ /<MadCap:dropDown class="rcode"/) { print "section already present in $topic\n"; exit 0; }
my $eol = $html =~ /\r\n/ ? "\r\n" : "\n";

# depth of the logo path: topics sit one folder below Content
my @lines = (
    '        <MadCap:dropDown class="rcode">',
    '            <MadCap:dropDownHead class="rcode">',
    '                <MadCap:dropDownHotspot class="rcode"><img class="rlogo" src="../resources/Images/rcode/r-logo.png" alt="R" title="R code for this example" style="width: 26px;height: 20px;" /> R code</MadCap:dropDownHotspot>',
    '            </MadCap:dropDownHead>',
    '            <MadCap:dropDownBody>',
    '                <p class="rcodenote">PLACEHOLDER</p>',
    '                <pre class="rcode" xml:space="preserve">PLACEHOLDER</pre>',
    '            </MadCap:dropDownBody>',
    '        </MadCap:dropDown>',
    '        <p>&#160;</p>',
);
my $section = join($eol, @lines) . $eol;

# the trailing run of paragraphs that hold nothing but one link, up to </body>
my $link = qr/[ \t]*<p>\s*<a\s[^>]*>[^<]*<\/a>\s*<\/p>\s*/;
if ($html =~ /^(.*?)((?:$link)+)(\s*<\/body>.*)$/s) {
    my ($before, $links, $after) = ($1, $2, $3);
    # make sure the run really is at the end: nothing but links between it and </body>
    $html = $before . $section . $links . $after;
    print "section added before the see-also links in $topic\n";
} elsif ($html =~ s/(\s*<\/body>)/$eol$section$1/) {
    print "no see-also links found: section added at the end of $topic\n";
} else {
    die "no </body> in $topic\n";
}
open(my $o, '>:raw', $topic) or die "cannot write $topic";
print $o $html;
close $o;
