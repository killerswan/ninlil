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

let getDocRaw (url:string) : string = 
   // This function doesn't return, at all, if the URL is wrong 
   // in some ways. TODO: fix. 

   let getpage (url:string) = 
      async {
         // see Expert F# at 383, etc.
         let req        = WebRequest.Create(url, Timeout=5)
         use! response  = req.AsyncGetResponse()
         use reader     = new StreamReader(response.GetResponseStream())
         return reader.ReadToEnd()
      }

   // get data as XML, return it
   (Async.RunSynchronously(getpage url))


// simple queries /////////////////////////////////////////////

// xml out
let readPosts (start:int) (num:int)       = getDocRaw <| api + "/read" +
                                                         "?start=" + (sprintf "%d" start) + 
                                                         "&num="   + (sprintf "%d" num) +
                                                         "&type="  + "photo"

// status out
let deletePost (id:string)                = getDocRaw <| api + "/delete" +
                                                         "?email="      + email + 
                                                         "&password="   + password + 
                                                         "&post-id="    + id
                     
// new id out
let reblogPost (id:string) (rkey:string)  = getDocRaw <| api + "/reblog" + 
                                                         "?email="      + email + 
                                                         "&password="   + password + 
                                                         "&post-id="    + id + 
                                                         "&reblog-key=" + rkey


// process XML results /////////////////////////////////////////////

// after getPosts
let processPosts postsXML =
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

   // posts.HasChildNodes
   let postsFound = 
      [
         for ii in 0..(num-1) do
            let post       = posts.ChildNodes.Item(ii)
            let id         = post.Attributes.GetNamedItem("id").Value
            let reblogkey  = post.Attributes.GetNamedItem("reblog-key").Value
            let date       = post.Attributes.GetNamedItem("date-gmt").Value
            yield (id, reblogkey, date, post)
      ]

   // display a post tuple
   let display (id, reblogkey, date, post:XmlNode) = 
      let pic = post.ChildNodes.Item(1).InnerText
      printfn "id: %s, %s, %s -> %s" id reblogkey date pic

   // print stats
   printfn "%d of %d, starting at %d" num total start

   // print all
   postsFound |> List.map display |> ignore

   (start, total, postsFound)


// compose read and process /////////////////////////////////////////////
let readAndProcPosts a b = readPosts a b |> processPosts


// test /////////////////////////////////////////////

// "2010-11-24 05:57:26 GMT" -> System.DateTime
let processDate (datestring: string) =
   (System.DateTime.ParseExact( (datestring.Split [| ' ' |]).[0], "yyyy-MM-dd", null )) 
   // methods, e.g.:
   // .Month, 
   // .Year

let test() = 
   // read some posts
   let (start, total, posts) = readPosts 6666 4 |> processPosts

   // reblog those and delete original posts
   (posts |> List.map (fun (id, rkey, datestring, post) ->
         reblogPost id rkey   |> ignore
         deletePost id        |> ignore

         let date = processDate datestring

         (date.Year, date.Month)))


// agents /////////////////////////////////////////////

let reader = 
   MailboxProcessor.Start(
      fun inbox ->
         let rec loop() = 
            inbox.Scan(
               function 
               | ((start: int), (count: int)) -> 
                  Some(async { 
                                 readPosts start count 
                                 |> processPosts 
                                 |> ignore
                                 return! loop() 
                  })
               //| _ -> None
            )
         loop()
      )

//reader.Post (1,5)


// find range to consider ////////////////////////////////

// date of post
let dateOfPost (index: int) : int*int = 
   let (start, total, posts) = readPosts index 1 |> processPosts

   // srsly, TODO: make this post tuple a type
   let (_,_,datestring,_) = posts |> List.head 
   let date = processDate datestring

   (date.Year, date.Month)



// binsearch to find latest post before a given date
let rec search (target: int*int) (newest: int) (oldest: int) : int option = 

   // dates
   // infix
   let dateNewerThan (ya,ma) (yb,mb) : int =
      match ((ya, ma), (yb, mb)) with
      | _ when ya < yb -> -1
      | _ when ya = yb && ma < mb -> -1
      | _ when ya = yb && ma = mb -> 0
      | _ when ya = yb && ma > mb -> 1
      | _ when ya > yb -> 1
      | _ -> 0 // humbug: silence warnings

   // if we have a match, just step to the latest match
   let rec walkToNewestMatch start =
      match (dateNewerThan (dateOfPost (start-1)) target) with
      | -1 -> None                        // if we're stepping backwards, oops
      |  0 -> walkToNewestMatch (start-1)
      |  1 -> Some(start)
      |  _ -> None // humbug
      
   let middle = (newest + oldest) / 2   // overflow, but nobody has that many posts

   if newest = oldest then
      match (dateNewerThan (dateOfPost middle) target) with
      | -1 -> Some(middle)
      |  0 -> walkToNewestMatch middle
      |  1 -> Some(middle+1)
      |  _ -> None // humbug
      (*
      match (dateNewerThan (dateOfPost middle) target) with
      | -1 -> match (dateNewerThan (dateOfPost (middle-1)) target) with
              |  1 -> Some(middle)
              |  _ -> None
      |  0 -> walkToNewestMatch middle
      |  1 -> match (dateNewerThan (dateOfPost (middle+1)) target) with
              | -1 -> Some(middle+1)
              |  _ -> None
      |  _ -> None // humbug
      *)
   else
      match (dateNewerThan (dateOfPost middle) target) with
      | -1 -> search target newest (middle-1)
      |  0 -> walkToNewestMatch middle
      |  1 -> search target (middle+1) oldest
      |  _ -> None // humbug


// get the most recent post on a given date
let cutoff (year: int) (month: int) = 

   // start with latest post
   let (start, total, posts) = readPosts 1 1 |> processPosts

   // find where the end of the date we care about is
   let foundDatePostNum = search (year,month) start total

   foundDatePostNum


cutoff 2010 12 |> ignore
readPosts 847 2 |> processPosts |> ignore





