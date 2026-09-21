#!/usr/bin/perl
# Exact text replacements in a help topic, each of which must occur exactly once (or N times with "N*" before the text).
# usage: perl help-subst.pl <topic.htm> "<from>" "<to>" ["<from>" "<to>" ...]
use strict;
use warnings;
my $file = shift @ARGV;
die "pairs of from/to expected\n" if @ARGV % 2;
open(my $i, '<:raw', $file) or die "cannot read $file";
local $/; my $c = <$i>; close $i;
while (@ARGV) {
    my ($from, $to) = (shift @ARGV, shift @ARGV);
    my $expect = 1;
    if ($from =~ s/^(\d+)\*//) { $expect = $1; }
    my $n = () = $c =~ /\Q$from\E/g;
    die "$file: expected $expect of [$from], found $n - nothing written\n" unless $n == $expect;
    $c =~ s/\Q$from\E/$to/g;
}
open(my $o, '>:raw', $file) or die; print $o $c; close $o;
print "$file: updated\n";
