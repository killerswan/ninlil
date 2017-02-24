Ninlil
Kevin Cantu <me@kevincantu.org>
June 2012


This F# tool to delete older Tumblr posts -- those whose context is
long gone -- is named for a Sumerian goddess of the air.  All things
are fleeting...  

Feel free to build upon these: tell me if you make 
anything exciting!


Tumblr.fs -- contains Ninlil.Tumblr, with Post and API types 
             to simplify calls to the Tumblr API for a given account

Ninlil.fs -- contains Ninlil.Main, with a program which finds all 
             posts older than a certain date and deletes them :D


A note about compiling: after installing F# on Ubuntu (which required `mono-devel`),
I found that now things work better when the compiler is given `--standalone`.  :D




