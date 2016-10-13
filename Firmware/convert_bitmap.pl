#!/usr/bin/perl

open IN, "xxd -i fpga_bitmap|";
open OUT, ">fpga_bitmap.h";

while(<IN>) {
	s/unsigned char/const PROGMEM unsigned char/;
	s/unsigned int/unsigned long/;
	print OUT;
}