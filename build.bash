#!/bin/bash
# I've been using F# on Mono, so having Bash and so on makes sense...

ME="$(basename "$0")"

# find a compiler
if [ $(which runfsc) ]
then
   FSC=runfsc
elif [ $(which fsharpc) ]
then
   FSC=fsharpc
elif [ $(which fsc) ]
then
   FSC=fsc
else
   echo "$ME: Cannot find an F# compiler..."
   exit 1
fi

# name our program
EXE=Ninlil.exe
if [ -f "$EXE" ]
then
   mv "$EXE" "$EXE.old"
fi

# build
$FSC --warn:4 --nologo --optimize --out:"$EXE" HTTP.fs Tumblr.fs Ninlil.fs


