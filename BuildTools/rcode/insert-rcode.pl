#!/usr/bin/perl
# Puts an R script into the "R code" section of a help topic, escaped for XHTML.
# usage: perl insert-rcode.pl <topic.htm> <script.R> "<note text>"
use strict;
use warnings;

my ($topic, $script, $note) = @ARGV;
open(my $t, '<:raw', $topic) or die "cannot read $topic";
local $/;
my $html = <$t>;
close $t;
my $eol = $html =~ /\r\n/ ? "\r\n" : "\n";

open(my $s, '<:raw', $script) or die "cannot read $script";
my $code = <$s>;
close $s;
$code =~ s/\r\n/\n/g;
$code =~ s/\s+\z//;
die "non-ASCII character in the script\n" if $code =~ /[^\x09\x0A\x20-\x7E]/;
$code =~ s/&/&amp;/g;
$code =~ s/</&lt;/g;
$code =~ s/>/&gt;/g;
$code =~ s/\n/$eol/g;

my $n = 0;
$n += ($html =~ s{(<p class="rcodenote">).*?(</p>)}{$1$note$2}s);
$n += ($html =~ s{(<pre class="rcode" xml:space="preserve">).*?(</pre>)}{$1$code$2}s);
die "expected to replace 2 places, replaced $n\n" unless $n == 2;

open(my $o, '>:raw', $topic) or die "cannot write $topic";
print $o $html;
close $o;
print "inserted ", scalar(split(/\n/, $code)), " lines of R into $topic (line ends: ", ($eol eq "\r\n" ? 'CRLF' : 'LF'), ")\n";
