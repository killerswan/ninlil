// Copyright (c) Kevin Cantu <me@kevincantu.org>
//
// redux - manipulate old Tumblr posts


// dependencies /////////////////////////////////////////////

open System.Collections.Generic
open System.Net
open System.IO
open System.Threading
open System.Text.RegularExpressions
open System.Xml
open System.Drawing


// command line args /////////////////////////////////////////////

let ( api, email, password ) = match System.Environment.GetCommandLineArgs() with
                               | [| _; blog; email; password |] -> 
                                    ( ("http://" + blog + ".tumblr.com/api"), email, password )
                               | _ -> 
                                    failwithf "Usage: mono exe BLOG EMAIL PASSWORD"


// fetch a URL /////////////////////////////////////////////
(*
   With async, is it more reasonable to just let things fail early, here,
   rather than continuing? 
   
   TODO: How can I see both the error and its type?

   async {
      try
         ...
      with
         | :? System.UriFormatException -> return ""
         | :? System.Net.WebException   -> return ""
   }
*)

let getDocRaw (url:string) : string = 

   // get data as XML, return it
   // see Expert F# at 383, etc.
   (Async.RunSynchronously(async {
      let req        = WebRequest.Create(url, Timeout=5)
      use! response  = req.AsyncGetResponse()
      use reader     = new StreamReader(response.GetResponseStream())
      return reader.ReadToEnd()
   }))


// simple queries /////////////////////////////////////////////

// xml out
let readPosts ((start,num): int*int) : string      = getDocRaw <| api + "/read" +
                                                         "?start=" + (sprintf "%d" start) + 
                                                         "&num="   + (sprintf "%d" num) +
                                                         "&type="  + "photo"

// status out
let deletePost (id:string) : string                = getDocRaw <| api + "/delete" +
                                                         "?email="      + email + 
                                                         "&password="   + password + 
                                                         "&post-id="    + id
                     
// new id out
let reblogPost (id:string) (rkey:string) : string  = getDocRaw <| api + "/reblog" + 
                                                         "?email="      + email + 
                                                         "&password="   + password + 
                                                         "&post-id="    + id + 
                                                         "&reblog-key=" + rkey


// process XML results /////////////////////////////////////////////

// after getPosts
let processPosts (postsXML) =
   let doc = new XmlDocument()
   postsXML |> doc.LoadXml // so doc is mutable?

   // add prettier printing
   //fsi.AddPrinter( fun (x:XmlNode) -> x.OuterXml );;

   // process the data
   let tumblr = doc.ChildNodes.Item(1)
   let posts  = tumblr.ChildNodes.Item(1)

   // overall statistics
   let start = System.Convert.ToInt32(posts.Attributes.GetNamedItem("start").Value)
   let total = System.Convert.ToInt32(posts.Attributes.GetNamedItem("total").Value)
   let num   = posts.ChildNodes.Count

   // "2010-11-24 05:57:26 GMT"
   let processDate (datestring: string) : System.DateTime =
      (System.DateTime.ParseExact( (datestring.Split [| ' ' |]).[0], "yyyy-MM-dd", null )) 

   // posts.HasChildNodes
   let postsFound = 
      [
         for ii in 0..(num-1) do
            let post       = posts.ChildNodes.Item(ii)
            let id         = post.Attributes.GetNamedItem("id").Value
            let reblogkey  = post.Attributes.GetNamedItem("reblog-key").Value
            let date       = post.Attributes.GetNamedItem("date-gmt").Value |> processDate
            yield (id, reblogkey, date, post)
      ]

   // display a post tuple
   let display (id, reblogkey, date, post:XmlNode) = 
      let pic = post.ChildNodes.Item(1).InnerText
      eprintfn "-> id: %s, %s, %s\n   %s" id reblogkey (date.ToString()) pic

   // print stats
   eprintfn "Got %d to %d of %d" start (start+num-1) total |> ignore

   // print all
   postsFound |> List.map display |> ignore

   (start, total, postsFound)


// compose read and process /////////////////////////////////////////////
let readAndProcessPosts = readPosts >> processPosts


// agent /////////////////////////////////////////////

(* If I understand correctly: this agent reads and processes
   one message at a time.
*)
   


// THE AGENT WORKS IN FSI o_O

type Message = 
   | ToRead of int * int
   | ToPrint of string
   
let agent = 
   MailboxProcessor.Start(
      fun inbox ->
         let rec loop() = 

            // handle the next message
            inbox.Scan(
               function 
               | ToRead (start, count) -> Some(async { 
                     eprintfn "reading: (%d, %d)" start count
                     readAndProcessPosts (start, count) |> ignore

                     // dither
                     Async.RunSynchronously(Async.Sleep (10*1000)) |> ignore

                     // during the time when we're asleep, the whole agent is so                  

                     return! loop() 
                 })
               | ToPrint (s) -> Some(async {
                     do eprintfn "printing: %s" s
                     //agent.Post <| ToRead (570,1) // can't be called this way

                     // dither
                     Async.RunSynchronously(Async.Sleep (20*1000)) |> ignore

                     return! loop()
                 })
            )


         loop()
      )

printfn "a"
agent.Post <| ToRead (1, 5)  // this 
printfn "b"
agent.Post <| ToPrint "omfg" // this and
printfn "c"
agent.Post <| ToRead (47, 2) // this do not block
printfn "d"



// range to consider ////////////////////////////////

// get the most recent post on a given date
let rangeEndingIn (targetDate: System.DateTime) : int*int = 

   // date of post /////////////////////////////////////////////
   let dateOfPost (index: int) : System.DateTime = 
      let (start, total, posts) = readAndProcessPosts (index, 1)

      // srsly, TODO: make this post tuple a type
      let (_,_,date,_) = posts |> List.head 
      date

   // if we have a match for the right date, step the the latest post on that date
   let rec walkToNewestMatch (start: int) (target: System.DateTime) : int =
      let nextPostDate = dateOfPost (start-1)
      match (nextPostDate > target) with
      | true  -> start
      | false -> walkToNewestMatch (start-1) target
      
   // binsearch to find latest post before a given date
   let rec findCutoff (target: System.DateTime) (newest: int) (oldest: int) : int = 

      let middle = (newest + oldest) / 2

      let middleDate : System.DateTime = dateOfPost middle

      match (middleDate, target) with
      | a,b when newest =  oldest && a < b -> middle
      | a,b when newest =  oldest && a = b -> walkToNewestMatch middle target
      | a,b when newest =  oldest && a > b -> middle+1
      | a,b when newest <> oldest && a < b -> findCutoff target newest (middle-1)
      | a,b when newest <> oldest && a = b -> walkToNewestMatch middle target    // combine above
      | a,b when newest <> oldest && a > b -> findCutoff target (middle+1) oldest
      | _                                  -> -1 // humbug

   // get the latest post
   let (startingPostNumber, total, posts) = readAndProcessPosts (1, 1)

   // find where the end of the date we care about is
   let first = total
   let last = findCutoff targetDate startingPostNumber total

   (first, last)


// tests /////////////////////////////////////////////

let testPostReblogging() = 
   // read some posts
   let (start, total, posts) = readAndProcessPosts (6666, 4)

   // reblog those and delete original posts
   (posts |> List.map (fun (id, rkey, datestring, post) ->
         reblogPost id rkey   |> ignore
         deletePost id        |> ignore

         (*
         let date = processDate datestring

         (date.Year, date.Month)
         *)

         () ))

let testFindingCutoff() = 
   printfn ""

   let (_,p1) = rangeEndingIn (System.DateTime(2010,12,31))
   printfn "The latest post on or before that date is #%d" p1
   printfn ""

   let (_,p2) = rangeEndingIn (System.DateTime(2011,7,8))
   printfn "The latest post on or before that date is #%d" p2
   printfn ""

   readAndProcessPosts (1187,5) |> ignore



//readAndProcessPosts (7, 2) |> ignore



